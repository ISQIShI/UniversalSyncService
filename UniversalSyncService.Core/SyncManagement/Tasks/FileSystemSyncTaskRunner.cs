using Microsoft.Extensions.Logging;
using UniversalSyncService.Abstractions.SyncItems;
using UniversalSyncService.Abstractions.SyncManagement.ConfigNodes;
using UniversalSyncService.Abstractions.SyncManagement.Engine;
using UniversalSyncService.Abstractions.SyncManagement.History;
using UniversalSyncService.Abstractions.SyncManagement.Plans;
using UniversalSyncService.Abstractions.SyncManagement.Tasks;
using UniversalSyncService.Core.Providers;
using UniversalSyncService.Core.SyncManagement.Engine;

namespace UniversalSyncService.Core.SyncManagement.Tasks;

/// <summary>
/// 普通文件系统任务执行器。
/// 这里把“计划任务”真正推进到本地文件系统复制、删除和历史落盘。
/// </summary>
public sealed class FileSystemSyncTaskRunner : ISyncTaskRunner
{
    private readonly NodeProviderRegistry _nodeProviderRegistry;
    private readonly ISyncAlgorithmEngine _syncAlgorithmEngine;
    private readonly IConflictResolver _conflictResolver;
    private readonly ISyncHistoryManager _syncHistoryManager;
    private readonly ILogger<FileSystemSyncTaskRunner> _logger;

    public FileSystemSyncTaskRunner(
        NodeProviderRegistry nodeProviderRegistry,
        ISyncAlgorithmEngine syncAlgorithmEngine,
        IConflictResolver conflictResolver,
        ISyncHistoryManager syncHistoryManager,
        ILogger<FileSystemSyncTaskRunner> logger)
    {
        _nodeProviderRegistry = nodeProviderRegistry;
        _syncAlgorithmEngine = syncAlgorithmEngine;
        _conflictResolver = conflictResolver;
        _syncHistoryManager = syncHistoryManager;
        _logger = logger;
    }

    public bool CanRun(string syncItemType)
    {
        return SyncItemKinds.IsFileSystem(syncItemType);
    }

    public TaskExecutionRequirement GetExecutionRequirement(
        string syncItemType,
        NodeConfiguration masterNode,
        NodeConfiguration slaveNode,
        SyncPlanSlaveConfiguration slaveConfiguration)
    {
        ArgumentNullException.ThrowIfNull(masterNode);
        ArgumentNullException.ThrowIfNull(slaveNode);
        ArgumentNullException.ThrowIfNull(slaveConfiguration);

        var normalizedSyncItemKind = SyncItemKinds.Normalize(syncItemType);
        if (!CanRun(normalizedSyncItemKind))
        {
            return TaskExecutionRequirement.MissingSyncItemImplementation;
        }

        if (!_nodeProviderRegistry.CanCreate(masterNode)
            || !_nodeProviderRegistry.CanCreate(slaveNode)
            || !_nodeProviderRegistry.SupportsSyncItemKind(masterNode, normalizedSyncItemKind)
            || !_nodeProviderRegistry.SupportsSyncItemKind(slaveNode, normalizedSyncItemKind))
        {
            return TaskExecutionRequirement.MissingNodeImplementation;
        }

        return TaskExecutionRequirement.Ready;
    }

    public async Task<SyncTaskResult> ExecuteAsync(ISyncTask task, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(task);

        var masterConfiguration = CreateScopedNodeConfiguration(task.MasterNode, task.TargetPath);
        var slaveConfiguration = CreateScopedNodeConfiguration(task.SlaveNode, task.SourcePath);

        var masterNode = await _nodeProviderRegistry.CreateAsync(masterConfiguration, cancellationToken);
        var slaveNode = await _nodeProviderRegistry.CreateAsync(slaveConfiguration, cancellationToken);

        await masterNode.ConnectAsync(cancellationToken);
        await slaveNode.ConnectAsync(cancellationToken);

        try
        {
            task.ReportProgress(0, 0, 0, 0, null, "正在扫描主节点与从节点文件系统");

            var masterItems = await masterNode.GetSyncItemsAsync(cancellationToken).ToListAsync(cancellationToken);
            var slaveItems = await slaveNode.GetSyncItemsAsync(cancellationToken).ToListAsync(cancellationToken);

            var masterByPath = masterItems.ToDictionary(item => item.Metadata.Path, StringComparer.OrdinalIgnoreCase);
            var slaveByPath = slaveItems.ToDictionary(item => item.Metadata.Path, StringComparer.OrdinalIgnoreCase);
            var currentPaths = new HashSet<string>(masterByPath.Keys, StringComparer.OrdinalIgnoreCase);
            currentPaths.UnionWith(slaveByPath.Keys);

            var masterHistory = await _syncHistoryManager.GetPreviousSyncHistoryAsync(task.PlanId, task.MasterNode.Id);
            var slaveHistory = await _syncHistoryManager.GetPreviousSyncHistoryAsync(task.PlanId, task.SlaveNode.Id);
            var masterHistoryByPath = masterHistory
                .OrderByDescending(entry => entry.SyncVersion)
                .GroupBy(entry => entry.Metadata.Path, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            var slaveHistoryByPath = slaveHistory
                .OrderByDescending(entry => entry.SyncVersion)
                .GroupBy(entry => entry.Metadata.Path, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            // 生命周期契约：运行时 contexts 只允许来自“当前扫描 + 显式删除候选”。
            // 历史只作为 anchor 查询，且仅限当前扫描路径，不允许注入新增路径。
            var masterHistoryAnchors = currentPaths
                .Where(masterHistoryByPath.ContainsKey)
                .ToDictionary(path => path, path => masterHistoryByPath[path], StringComparer.OrdinalIgnoreCase);
            var slaveHistoryAnchors = currentPaths
                .Where(slaveHistoryByPath.ContainsKey)
                .ToDictionary(path => path, path => slaveHistoryByPath[path], StringComparer.OrdinalIgnoreCase);

            // 显式删除候选：仅允许“当前未扫描到，但上轮至少一侧仍是 Exists”的路径进入本轮决策。
            // 已 tombstoned(Deleted) 的历史条目不会再被注入 contexts，防止 resurrection。
            var explicitDeleteCandidatePaths = masterHistoryByPath
                .Where(pair =>
                    !currentPaths.Contains(pair.Key)
                    && pair.Value.State == FileHistoryState.Exists)
                .Select(pair => pair.Key)
                .Union(
                    slaveHistoryByPath
                        .Where(pair =>
                            !currentPaths.Contains(pair.Key)
                            && pair.Value.State == FileHistoryState.Exists)
                        .Select(pair => pair.Key),
                    StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var decisionPaths = new HashSet<string>(currentPaths, StringComparer.OrdinalIgnoreCase);
            decisionPaths.UnionWith(explicitDeleteCandidatePaths);

            var contexts = decisionPaths
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path =>
                {
                    masterByPath.TryGetValue(path, out var masterItem);
                    slaveByPath.TryGetValue(path, out var slaveItem);
                    var isExplicitDeleteCandidate = explicitDeleteCandidatePaths.Contains(path);

                    SyncHistoryEntry? masterHistoryEntry = null;
                    SyncHistoryEntry? slaveHistoryEntry = null;
                    if (isExplicitDeleteCandidate)
                    {
                        masterHistoryByPath.TryGetValue(path, out masterHistoryEntry);
                        slaveHistoryByPath.TryGetValue(path, out slaveHistoryEntry);
                    }
                    else
                    {
                        masterHistoryAnchors.TryGetValue(path, out masterHistoryEntry);
                        slaveHistoryAnchors.TryGetValue(path, out slaveHistoryEntry);
                    }

                    return new SyncPathSyncContext(
                        path,
                        masterItem?.Metadata,
                        slaveItem?.Metadata,
                        masterHistoryEntry,
                        slaveHistoryEntry,
                        isExplicitDeleteCandidate);
                })
                .ToList();
            var contextByPath = contexts.ToDictionary(context => context.Path, StringComparer.OrdinalIgnoreCase);

            var decisions = await _syncAlgorithmEngine.CalculateDecisionsAsync(contexts, task.SyncMode);
            if (decisions.Count == 0)
            {
                task.ReportProgress(0, 0, 0, 0, null, "未发现可同步项");
                return SyncTaskResult.NoChanges;
            }

            var deleteGuardDecision = EvaluateDeleteGuard(task, decisions, contexts.Count);
            if (!deleteGuardDecision.ShouldProceed)
            {
                task.AddError(deleteGuardDecision.ErrorMessage!);
                task.ReportProgress(0, decisions.Count, 0, 0, null, "删除守卫触发，任务已停止等待管理员处理");
                return SyncTaskResult.Failed;
            }

            var processedFiles = 0;
            var changed = false;
            foreach (var decisionEntry in decisions.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var itemIdentity = decisionEntry.Key;
                masterByPath.TryGetValue(itemIdentity, out var masterItem);
                slaveByPath.TryGetValue(itemIdentity, out var slaveItem);
                var decision = decisionEntry.Value;
                contextByPath.TryGetValue(itemIdentity, out var syncContext);
                if (decision == SyncDecision.Conflict && syncContext is not null)
                {
                    decision = await ResolveConflictDecisionAsync(task, syncContext);
                }
                task.ReportProgress(processedFiles, decisions.Count, 0, 0, itemIdentity, $"正在处理决策：{decision}");

                switch (decision)
                {
                    case SyncDecision.DoNothing:
                        break;
                    case SyncDecision.Push:
                        if (masterItem is not null)
                        {
                            await slaveNode.UploadAsync(masterItem, cancellationToken);
                            changed = true;
                        }
                        break;
                    case SyncDecision.Pull:
                        if (slaveItem is not null)
                        {
                            await masterNode.UploadAsync(slaveItem, cancellationToken);
                            changed = true;
                        }
                        break;
                    case SyncDecision.DeleteLocal:
                        // 【删除语义收敛】删除决策应由节点契约统一承接，
                        // 不能只对 LocalNode 生效，否则 OneDrive / 后续远端节点的删除会被静默跳过。
                        if ((masterNode.Capabilities & Abstractions.Nodes.NodeCapabilities.CanDelete) != 0)
                        {
                            await masterNode.DeleteAsync(itemIdentity, cancellationToken);
                            changed = true;
                        }
                        break;
                    case SyncDecision.DeleteRemote:
                        if ((slaveNode.Capabilities & Abstractions.Nodes.NodeCapabilities.CanDelete) != 0)
                        {
                            await slaveNode.DeleteAsync(itemIdentity, cancellationToken);
                            changed = true;
                        }
                        break;
                    case SyncDecision.CleanHistory:
                        changed = true;
                        break;
                    case SyncDecision.Conflict:
                    case SyncDecision.ConflictRename:
                        task.AddError($"文件 {itemIdentity} 出现冲突，当前未自动解决。");
                        return SyncTaskResult.Conflict;
                }

                processedFiles++;
            }

            // 统一在操作完成后重新扫描，确保写入历史的是“最终状态”而不是过程状态。
            var finalMasterItems = await masterNode.GetSyncItemsAsync(cancellationToken).ToListAsync(cancellationToken);
            var finalSlaveItems = await slaveNode.GetSyncItemsAsync(cancellationToken).ToListAsync(cancellationToken);
            await SaveHistoryAsync(task, finalMasterItems, finalSlaveItems, masterHistoryByPath, slaveHistoryByPath);

            if (task.DeletionPolicy.AllowThresholdBreachForCurrentRun)
            {
                // 一次性阈值越权只允许当前运行消耗，成功完成后立即清除，避免复用同一任务对象时持续生效。
                task.DeletionPolicy.AllowThresholdBreachForCurrentRun = false;
                task.DeletionPolicy.ThresholdOverrideReason = null;
            }

            task.ReportProgress(decisions.Count, decisions.Count, 0, 0, null, changed ? "同步完成" : "无需同步变更");
            return changed ? SyncTaskResult.Success : SyncTaskResult.NoChanges;
        }
        finally
        {
            await masterNode.DisconnectAsync(cancellationToken);
            await slaveNode.DisconnectAsync(cancellationToken);
        }
    }

    private async Task SaveHistoryAsync(
        ISyncTask task,
        IReadOnlyList<ISyncItem> masterItems,
        IReadOnlyList<ISyncItem> slaveItems,
        IReadOnlyDictionary<string, SyncHistoryEntry> previousMasterHistoryByPath,
        IReadOnlyDictionary<string, SyncHistoryEntry> previousSlaveHistoryByPath)
    {
        var nextVersion = await _syncHistoryManager.GetLatestVersionAsync(task.PlanId) + 1;
        var now = DateTimeOffset.Now;
        var historyEntries = new List<SyncHistoryEntry>();
        var masterPaths = masterItems.Select(item => item.Metadata.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var slavePaths = slaveItems.Select(item => item.Metadata.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);

        historyEntries.AddRange(masterItems.Select(item => new SyncHistoryEntry(
            Guid.NewGuid().ToString("N"),
            task.PlanId,
            task.Id,
            task.MasterNode.Id,
            item.Metadata,
            FileHistoryState.Exists,
            now,
            nextVersion)));

        historyEntries.AddRange(slaveItems.Select(item => new SyncHistoryEntry(
            Guid.NewGuid().ToString("N"),
            task.PlanId,
            task.Id,
            task.SlaveNode.Id,
            item.Metadata,
            FileHistoryState.Exists,
            now,
            nextVersion)));

        historyEntries.AddRange(previousMasterHistoryByPath.Values
            .Where(entry => entry.State != FileHistoryState.Deleted && !masterPaths.Contains(entry.Metadata.Path))
            .Select(entry => new SyncHistoryEntry(
                Guid.NewGuid().ToString("N"),
                task.PlanId,
                task.Id,
                task.MasterNode.Id,
                entry.Metadata,
                FileHistoryState.Deleted,
                now,
                nextVersion)));

        historyEntries.AddRange(previousSlaveHistoryByPath.Values
            .Where(entry => entry.State != FileHistoryState.Deleted && !slavePaths.Contains(entry.Metadata.Path))
            .Select(entry => new SyncHistoryEntry(
                Guid.NewGuid().ToString("N"),
                task.PlanId,
                task.Id,
                task.SlaveNode.Id,
                entry.Metadata,
                FileHistoryState.Deleted,
                now,
                nextVersion)));

        await _syncHistoryManager.SaveHistoryAsync(historyEntries);
        _logger.LogInformation("已写入同步历史。计划={PlanId}，版本={Version}，记录数={EntryCount}", task.PlanId, nextVersion, historyEntries.Count);
    }

    private NodeConfiguration CreateScopedNodeConfiguration(NodeConfiguration source, string? scopeBoundary)
    {
        var connectionSettings = new Dictionary<string, string>(source.ConnectionSettings, StringComparer.OrdinalIgnoreCase);
        if (connectionSettings.ContainsKey("RootPath") || !string.IsNullOrWhiteSpace(scopeBoundary))
        {
            connectionSettings["RootPath"] = _nodeProviderRegistry.ResolveScopeBoundary(source, scopeBoundary);
        }

        var scopedConfiguration = new NodeConfiguration(source.Id, source.Name, source.NodeType, connectionSettings, source.CreatedAt)
        {
            ModifiedAt = source.ModifiedAt,
            IsEnabled = source.IsEnabled,
            CustomOptions = source.CustomOptions is null
                ? null
                : new Dictionary<string, object>(source.CustomOptions, StringComparer.OrdinalIgnoreCase)
        };

        return scopedConfiguration;
    }

    private DeleteGuardDecision EvaluateDeleteGuard(
        ISyncTask task,
        IReadOnlyDictionary<string, SyncDecision> decisions,
        int totalContextCount)
    {
        var projectedDeleteCount = decisions.Values.Count(IsDestructiveDeleteDecision);
        if (projectedDeleteCount == 0)
        {
            return DeleteGuardDecision.Allow();
        }

        var policy = task.DeletionPolicy;
        var normalizedAbsoluteThreshold = Math.Max(1, policy.DeleteThreshold);
        var normalizedPercentThreshold = Math.Clamp(policy.PercentThreshold, 0.1d, 100d);
        var normalizedTotalContextCount = Math.Max(1, totalContextCount);
        var percentLimit = Math.Max(1, (int)Math.Ceiling(normalizedTotalContextCount * (normalizedPercentThreshold / 100d)));
        var effectiveThreshold = Math.Min(normalizedAbsoluteThreshold, percentLimit);
        var projectedDeletePercent = (double)projectedDeleteCount / normalizedTotalContextCount * 100d;

        if (projectedDeleteCount <= effectiveThreshold)
        {
            return DeleteGuardDecision.Allow();
        }

        _logger.LogWarning(
            "[AUDIT] 删除阈值触发。计划={PlanId} 任务={TaskId} 模式={Mode} 删除候选={DeleteCount} 上下文总量={TotalCount} 绝对阈值={AbsoluteThreshold} 百分比阈值={PercentThreshold}% 百分比阈值折算={PercentLimit} 生效阈值={EffectiveThreshold} 删除占比={DeletePercent:F2}%",
            task.PlanId,
            task.Id,
            policy.FailSafeMode,
            projectedDeleteCount,
            normalizedTotalContextCount,
            normalizedAbsoluteThreshold,
            normalizedPercentThreshold,
            percentLimit,
            effectiveThreshold,
            projectedDeletePercent);

        if (policy.AllowThresholdBreachForCurrentRun && !string.IsNullOrWhiteSpace(policy.ThresholdOverrideReason))
        {
            _logger.LogWarning(
                "[AUDIT] 管理员阈值越权已应用。计划={PlanId} 任务={TaskId} 模式={Mode} 原因={Reason}",
                task.PlanId,
                task.Id,
                policy.FailSafeMode,
                policy.ThresholdOverrideReason);
            return DeleteGuardDecision.Allow();
        }

        return policy.FailSafeMode switch
        {
            SyncPlanFailSafeMode.Ignore => HandleIgnoreMode(task, projectedDeleteCount, effectiveThreshold),
            SyncPlanFailSafeMode.Confirm => HandleConfirmMode(task, projectedDeleteCount, effectiveThreshold),
            _ => HandleBlockMode(task, projectedDeleteCount, effectiveThreshold)
        };
    }

    private DeleteGuardDecision HandleBlockMode(ISyncTask task, int projectedDeleteCount, int effectiveThreshold)
    {
        var message =
            $"删除守卫已阻断同步：计划 {task.PlanId} 的删除候选 {projectedDeleteCount} 超过阈值 {effectiveThreshold}。" +
            "请由管理员显式审批（配置 ThresholdOverrideReason + AllowThresholdBreachForCurrentRun）后重试。";
        _logger.LogError(
            "[AUDIT] fail-safe=BLOCK：阻断同步。计划={PlanId} 任务={TaskId} 删除候选={DeleteCount} 生效阈值={EffectiveThreshold}",
            task.PlanId,
            task.Id,
            projectedDeleteCount,
            effectiveThreshold);
        return DeleteGuardDecision.Block(message);
    }

    private DeleteGuardDecision HandleConfirmMode(ISyncTask task, int projectedDeleteCount, int effectiveThreshold)
    {
        var message =
            $"删除守卫进入确认态：计划 {task.PlanId} 的删除候选 {projectedDeleteCount} 超过阈值 {effectiveThreshold}，任务已暂停。" +
            "请填写管理员审批原因并启用 AllowThresholdBreachForCurrentRun 后重试。";
        _logger.LogWarning(
            "[AUDIT] fail-safe=CONFIRM：等待管理员确认。计划={PlanId} 任务={TaskId} 删除候选={DeleteCount} 生效阈值={EffectiveThreshold}",
            task.PlanId,
            task.Id,
            projectedDeleteCount,
            effectiveThreshold);
        return DeleteGuardDecision.Block(message);
    }

    private DeleteGuardDecision HandleIgnoreMode(ISyncTask task, int projectedDeleteCount, int effectiveThreshold)
    {
        _logger.LogWarning(
            "[AUDIT] fail-safe=IGNORE：继续执行超阈值删除。计划={PlanId} 任务={TaskId} 删除候选={DeleteCount} 生效阈值={EffectiveThreshold}。仅建议在 dev/test 环境使用。",
            task.PlanId,
            task.Id,
            projectedDeleteCount,
            effectiveThreshold);
        return DeleteGuardDecision.Allow();
    }

    private static bool IsDestructiveDeleteDecision(SyncDecision decision)
    {
        return decision is SyncDecision.DeleteLocal or SyncDecision.DeleteRemote;
    }

    private async Task<SyncDecision> ResolveConflictDecisionAsync(ISyncTask task, SyncPathSyncContext context)
    {
        var conflict = new SyncConflict(
            context.Path,
            context.MasterMetadata is null ? null : SyncItemFileStateSnapshot.FromMetadata(context.MasterMetadata),
            context.SlaveMetadata is null ? null : SyncItemFileStateSnapshot.FromMetadata(context.SlaveMetadata),
            context.MasterHistoryEntry is null ? null : SyncItemFileStateSnapshot.FromMetadata(context.MasterHistoryEntry.Metadata),
            context.SlaveHistoryEntry is null ? null : SyncItemFileStateSnapshot.FromMetadata(context.SlaveHistoryEntry.Metadata),
            "检测到双方变更冲突，正在根据策略尝试解析。");

        var resolvedDecision = await _conflictResolver.ResolveAsync(conflict, task.ConflictResolutionStrategy);
        if (resolvedDecision == SyncDecision.Conflict)
        {
            task.AddError($"文件 {context.Path} 出现冲突，当前策略 {task.ConflictResolutionStrategy} 无法自动解决。");
        }

        return resolvedDecision;
    }

    private sealed class DeleteGuardDecision
    {
        private DeleteGuardDecision(bool shouldProceed, string? errorMessage)
        {
            ShouldProceed = shouldProceed;
            ErrorMessage = errorMessage;
        }

        public bool ShouldProceed { get; }

        public string? ErrorMessage { get; }

        public static DeleteGuardDecision Allow()
        {
            return new DeleteGuardDecision(true, null);
        }

        public static DeleteGuardDecision Block(string errorMessage)
        {
            return new DeleteGuardDecision(false, errorMessage);
        }
    }
}

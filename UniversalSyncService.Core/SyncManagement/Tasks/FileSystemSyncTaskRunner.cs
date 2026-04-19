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
            var paths = new HashSet<string>(masterItems.Select(item => item.Metadata.Path), StringComparer.OrdinalIgnoreCase);
            paths.UnionWith(slaveItems.Select(item => item.Metadata.Path));
            paths.UnionWith(masterHistoryByPath.Keys);
            paths.UnionWith(slaveHistoryByPath.Keys);

            var masterByPath = masterItems.ToDictionary(item => item.Metadata.Path, StringComparer.OrdinalIgnoreCase);
            var slaveByPath = slaveItems.ToDictionary(item => item.Metadata.Path, StringComparer.OrdinalIgnoreCase);
            var contexts = paths
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path =>
                {
                    masterByPath.TryGetValue(path, out var masterItem);
                    slaveByPath.TryGetValue(path, out var slaveItem);
                    masterHistoryByPath.TryGetValue(path, out var masterHistoryEntry);
                    slaveHistoryByPath.TryGetValue(path, out var slaveHistoryEntry);

                    return new SyncPathSyncContext(
                        path,
                        masterItem?.Metadata,
                        slaveItem?.Metadata,
                        masterHistoryEntry,
                        slaveHistoryEntry);
                })
                .ToList();
            var contextByPath = contexts.ToDictionary(context => context.Path, StringComparer.OrdinalIgnoreCase);

            var decisions = await _syncAlgorithmEngine.CalculateDecisionsAsync(contexts, task.SyncMode);
            if (decisions.Count == 0)
            {
                task.ReportProgress(0, 0, 0, 0, null, "未发现可同步项");
                return SyncTaskResult.NoChanges;
            }

            var processedFiles = 0;
            var changed = false;
            foreach (var decisionEntry in decisions.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var relativePath = decisionEntry.Key;
                masterByPath.TryGetValue(relativePath, out var masterItem);
                slaveByPath.TryGetValue(relativePath, out var slaveItem);
                var decision = decisionEntry.Value;
                contextByPath.TryGetValue(relativePath, out var syncContext);
                if (decision == SyncDecision.Conflict && syncContext is not null)
                {
                    decision = await ResolveConflictDecisionAsync(task, syncContext);
                }
                task.ReportProgress(processedFiles, decisions.Count, 0, 0, relativePath, $"正在处理决策：{decision}");

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
                            await masterNode.DeleteAsync(relativePath, cancellationToken);
                            changed = true;
                        }
                        break;
                    case SyncDecision.DeleteRemote:
                        if ((slaveNode.Capabilities & Abstractions.Nodes.NodeCapabilities.CanDelete) != 0)
                        {
                            await slaveNode.DeleteAsync(relativePath, cancellationToken);
                            changed = true;
                        }
                        break;
                    case SyncDecision.CleanHistory:
                        changed = true;
                        break;
                    case SyncDecision.Conflict:
                    case SyncDecision.ConflictRename:
                        task.AddError($"文件 {relativePath} 出现冲突，当前未自动解决。");
                        return SyncTaskResult.Conflict;
                }

                processedFiles++;
            }

            // 统一在操作完成后重新扫描，确保写入历史的是“最终状态”而不是过程状态。
            var finalMasterItems = await masterNode.GetSyncItemsAsync(cancellationToken).ToListAsync(cancellationToken);
            var finalSlaveItems = await slaveNode.GetSyncItemsAsync(cancellationToken).ToListAsync(cancellationToken);
            await SaveHistoryAsync(task, finalMasterItems, finalSlaveItems, masterHistoryByPath, slaveHistoryByPath);

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
            .Where(entry => !masterPaths.Contains(entry.Metadata.Path))
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
            .Where(entry => !slavePaths.Contains(entry.Metadata.Path))
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

    private NodeConfiguration CreateScopedNodeConfiguration(NodeConfiguration source, string? scopedRelativePath)
    {
        var connectionSettings = new Dictionary<string, string>(source.ConnectionSettings, StringComparer.OrdinalIgnoreCase);
        if (connectionSettings.ContainsKey("RootPath") || !string.IsNullOrWhiteSpace(scopedRelativePath))
        {
            connectionSettings["RootPath"] = _nodeProviderRegistry.ResolveScopedRoot(source, scopedRelativePath);
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
}

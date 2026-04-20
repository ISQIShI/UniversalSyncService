using Microsoft.Extensions.Logging;
using UniversalSyncService.Abstractions.Configuration;
using UniversalSyncService.Abstractions.SyncManagement.ConfigNodes;
using UniversalSyncService.Abstractions.SyncManagement.Plans;
using UniversalSyncService.Abstractions.SyncManagement.Tasks;
using UniversalSyncService.Core.Providers;
using UniversalSyncService.Core.SyncManagement.ConfigNodes;

namespace UniversalSyncService.Core.SyncManagement.Tasks;

public sealed class SyncTaskGenerator : ISyncTaskGenerator
{
    private readonly NodeRegistry _nodeRegistry;
    private readonly NodeProviderRegistry _nodeProviderRegistry;
    private readonly SyncTaskRunnerRegistry _syncTaskRunnerRegistry;
    private readonly ILoggerFactory _loggerFactory;

    public SyncTaskGenerator(
        NodeRegistry nodeRegistry,
        NodeProviderRegistry nodeProviderRegistry,
        SyncTaskRunnerRegistry syncTaskRunnerRegistry,
        ILoggerFactory loggerFactory)
    {
        _nodeRegistry = nodeRegistry;
        _nodeProviderRegistry = nodeProviderRegistry;
        _syncTaskRunnerRegistry = syncTaskRunnerRegistry;
        _loggerFactory = loggerFactory;
    }

    public async Task<IReadOnlyList<ISyncTask>> GenerateTasksAsync(SyncPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var validationResult = await ValidatePlanAsync(plan);
        if (!validationResult.IsValid)
        {
            throw new InvalidOperationException($"无法为同步计划 {plan.Id} 生成任务：{string.Join("；", validationResult.Errors)}");
        }

        _nodeRegistry.TryGet(ResolveMasterNodeId(plan), out NodeConfiguration? masterNode);
        var tasks = new List<ISyncTask>();
        foreach (var slaveConfiguration in plan.SlaveConfigurations)
        {
            _nodeRegistry.TryGet(slaveConfiguration.SlaveNodeId, out NodeConfiguration? slaveNode);
            if (masterNode is null || slaveNode is null)
            {
                continue;
            }

            var executionRequirement = string.IsNullOrWhiteSpace(plan.SyncItemType)
                ? TaskExecutionRequirement.MissingSyncItemImplementation
                : ResolveExecutionRequirement(plan, masterNode, slaveNode, slaveConfiguration);

            tasks.Add(new SyncTask(
                $"{plan.Id}:{slaveConfiguration.SlaveNodeId}",
                plan.Id,
                masterNode,
                slaveNode,
                slaveConfiguration.SyncMode,
                plan.SyncItemType,
                slaveConfiguration.SourcePath,
                slaveConfiguration.TargetPath,
                slaveConfiguration.ConflictResolutionStrategy,
                plan.DeletionPolicy,
                executionRequirement,
                _syncTaskRunnerRegistry.ExecuteAsync,
                _loggerFactory.CreateLogger<SyncTask>()));
        }

        if (plan.DeletionPolicy.AllowThresholdBreachForCurrentRun)
        {
            // 该标记是一次性审批信号，生成任务后立即消费，避免后续调度重复继承越权状态。
            plan.DeletionPolicy.AllowThresholdBreachForCurrentRun = false;
            plan.DeletionPolicy.ThresholdOverrideReason = null;
        }

        return tasks;
    }

    public Task<(bool IsValid, IReadOnlyList<string> Errors)> ValidatePlanAsync(SyncPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(plan.Name))
        {
            errors.Add("计划名称不能为空。");
        }

        var resolvedMasterNodeId = ResolveMasterNodeId(plan);

        if (!_nodeRegistry.TryGet(resolvedMasterNodeId, out _))
        {
            errors.Add($"未找到主节点配置：{resolvedMasterNodeId}。");
        }

        if (plan.SlaveConfigurations.Count == 0)
        {
            errors.Add("至少需要一个从节点配置。");
        }

        if (plan.DeletionPolicy.DeleteThreshold <= 0)
        {
            errors.Add("删除阈值必须大于 0。");
        }

        if (plan.DeletionPolicy.PercentThreshold <= 0 || plan.DeletionPolicy.PercentThreshold > 100)
        {
            errors.Add("删除百分比阈值必须在 (0, 100] 区间。");
        }

        if (plan.DeletionPolicy.AllowThresholdBreachForCurrentRun
            && string.IsNullOrWhiteSpace(plan.DeletionPolicy.ThresholdOverrideReason))
        {
            errors.Add("启用本轮阈值越权时必须提供审核原因。");
        }

        var duplicatedSlaveIds = plan.SlaveConfigurations
            .GroupBy(item => item.SlaveNodeId, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();
        foreach (var duplicatedSlaveId in duplicatedSlaveIds)
        {
            errors.Add($"从节点重复：{duplicatedSlaveId}。");
        }

        foreach (var slaveConfiguration in plan.SlaveConfigurations)
        {
            if (!_nodeRegistry.TryGet(resolvedMasterNodeId, out var resolvedMasterNode)
                || resolvedMasterNode is null
                || !_nodeProviderRegistry.SupportsScopeBoundary(resolvedMasterNode, slaveConfiguration.TargetPath))
            {
                errors.Add($"主节点作用域 {slaveConfiguration.TargetPath ?? "<root>"} 不受当前 Provider 支持。请调整为该 Provider 可接受的作用域边界。");
            }

            if (!_nodeRegistry.TryGet(slaveConfiguration.SlaveNodeId, out var resolvedSlaveNode)
                || resolvedSlaveNode is null
                || !_nodeProviderRegistry.SupportsScopeBoundary(resolvedSlaveNode, slaveConfiguration.SourcePath))
            {
                errors.Add($"从节点作用域 {slaveConfiguration.SourcePath ?? "<root>"} 不受当前 Provider 支持。请调整为该 Provider 可接受的作用域边界。");
            }

            if (string.Equals(resolvedMasterNodeId, slaveConfiguration.SlaveNodeId, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("主节点不能同时作为从节点。");
            }

            if (!_nodeRegistry.TryGet(slaveConfiguration.SlaveNodeId, out _))
            {
                errors.Add($"未找到从节点配置：{slaveConfiguration.SlaveNodeId}。");
            }
        }

        return Task.FromResult(((bool)(errors.Count == 0), (IReadOnlyList<string>)errors));
    }

    private TaskExecutionRequirement ResolveExecutionRequirement(
        SyncPlan plan,
        NodeConfiguration masterNode,
        NodeConfiguration slaveNode,
        SyncPlanSlaveConfiguration slaveConfiguration)
    {
        return _syncTaskRunnerRegistry.GetExecutionRequirement(plan.SyncItemType, masterNode, slaveNode, slaveConfiguration);
    }

    private static string ResolveMasterNodeId(SyncPlan plan)
    {
        return string.IsNullOrWhiteSpace(plan.MasterNodeId)
            ? SyncFrameworkOptions.DefaultHostNodeId
            : plan.MasterNodeId;
    }
}

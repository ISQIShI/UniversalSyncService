using Microsoft.Extensions.Logging;
using UniversalSyncService.Abstractions.Configuration;
using UniversalSyncService.Abstractions.SyncManagement.Engine;
using UniversalSyncService.Abstractions.SyncManagement.History;
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
                executionRequirement,
                _syncTaskRunnerRegistry.ExecuteAsync,
                _loggerFactory.CreateLogger<SyncTask>()));
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
            if (IsAbsoluteScopedPath(slaveConfiguration.TargetPath)
                && (!_nodeRegistry.TryGet(resolvedMasterNodeId, out var resolvedMasterNode)
                    || resolvedMasterNode is null
                    || !_nodeProviderRegistry.SupportsAbsoluteScopedPath(resolvedMasterNode)))
            {
                errors.Add($"主节点路径 {slaveConfiguration.TargetPath} 为绝对路径时，仅允许用于本地节点（Local/host-local）。");
            }

            if (IsAbsoluteScopedPath(slaveConfiguration.SourcePath)
                && (!_nodeRegistry.TryGet(slaveConfiguration.SlaveNodeId, out var resolvedSlaveNode)
                    || resolvedSlaveNode is null
                    || !_nodeProviderRegistry.SupportsAbsoluteScopedPath(resolvedSlaveNode)))
            {
                errors.Add($"从节点路径 {slaveConfiguration.SourcePath} 为绝对路径时，仅允许用于本地节点（Local/host-local）。");
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

    private static bool IsAbsoluteScopedPath(string? path)
    {
        return !string.IsNullOrWhiteSpace(path) && Path.IsPathRooted(path);
    }

    private static string ResolveMasterNodeId(SyncPlan plan)
    {
        return string.IsNullOrWhiteSpace(plan.MasterNodeId)
            ? SyncFrameworkOptions.DefaultHostNodeId
            : plan.MasterNodeId;
    }
}

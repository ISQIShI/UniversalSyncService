using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using UniversalSyncService.Abstractions.Configuration;
using UniversalSyncService.Abstractions.SyncManagement;
using UniversalSyncService.Abstractions.SyncManagement.Engine;
using UniversalSyncService.Abstractions.SyncManagement.Plans;
using UniversalSyncService.Abstractions.SyncManagement.Tasks;
using UniversalSyncService.Core.SyncManagement.ConfigNodes;

namespace UniversalSyncService.Core.SyncManagement;

public sealed class SyncPlanManager : ISyncPlanManager
{
    private readonly IConfigurationManagementService _configurationManagementService;
    private readonly ISyncEngine _syncEngine;
    private readonly ISyncTaskGenerator _syncTaskGenerator;
    private readonly NodeRegistry _nodeRegistry;
    private readonly ILogger<SyncPlanManager> _logger;
    private readonly ConcurrentDictionary<string, SyncPlan> _plans = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _planLock = new(1, 1);
    private volatile bool _isLoaded;
    private DateTimeOffset _lastLoadedConfigurationWriteTimeUtc;

    public SyncPlanManager(
        IConfigurationManagementService configurationManagementService,
        ISyncEngine syncEngine,
        ISyncTaskGenerator syncTaskGenerator,
        NodeRegistry nodeRegistry,
        ILogger<SyncPlanManager> logger)
    {
        _configurationManagementService = configurationManagementService;
        _syncEngine = syncEngine;
        _syncTaskGenerator = syncTaskGenerator;
        _nodeRegistry = nodeRegistry;
        _logger = logger;
    }

    public event Action<SyncPlan>? OnPlanCreated;

    public event Action<SyncPlan>? OnPlanUpdated;

    public event Action<SyncPlan>? OnPlanDeleted;

    public event Action<SyncPlan, bool>? OnPlanStatusChanged;

    public IReadOnlyList<SyncPlan> GetAllPlans()
    {
        EnsureLoaded();
        return _plans.Values.OrderBy(plan => plan.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public SyncPlan? GetPlanById(string planId)
    {
        ArgumentNullException.ThrowIfNull(planId);
        EnsureLoaded();
        _plans.TryGetValue(planId, out var plan);
        return plan;
    }

    public async Task<SyncPlan> CreatePlanAsync(
        string name,
        string? description,
        string masterNodeId,
        string syncItemType,
        IEnumerable<SyncPlanSlaveConfiguration> slaveConfigurations,
        SyncSchedule schedule,
        SyncPlanDeletionPolicy? deletionPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(masterNodeId);
        ArgumentNullException.ThrowIfNull(syncItemType);
        ArgumentNullException.ThrowIfNull(slaveConfigurations);
        ArgumentNullException.ThrowIfNull(schedule);

        EnsureLoaded();

        var resolvedMasterNodeId = ResolveMasterNodeId(masterNodeId);

        var plan = new SyncPlan(
            Guid.NewGuid().ToString("N"),
            name,
            resolvedMasterNodeId,
            syncItemType,
            slaveConfigurations.ToList(),
            schedule,
            deletionPolicy: deletionPolicy)
        {
            Description = description
        };

        if (plan.Schedule.TriggerType != SyncTriggerType.Manual)
        {
            plan.Schedule.NextScheduledTime = plan.Schedule.CalculateNextRunTime(plan.CreatedAt);
        }

        var validation = await ValidatePlanAsync(plan);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException($"同步计划配置无效：{string.Join("；", validation.Errors)}");
        }

        _plans[plan.Id] = plan;
        await PersistPlansAsync();
        LogDeletionPolicyInitialized(plan);
        OnPlanCreated?.Invoke(plan);
        return plan;
    }

    public async Task<SyncPlan> UpdatePlanAsync(string planId, Action<SyncPlan> updates)
    {
        ArgumentNullException.ThrowIfNull(planId);
        ArgumentNullException.ThrowIfNull(updates);

        EnsureLoaded();
        var currentPlan = GetRequiredPlan(planId);
        var candidatePlan = ClonePlan(currentPlan);
        updates(candidatePlan);
        candidatePlan.Touch();

        var validation = await ValidatePlanAsync(candidatePlan);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException($"同步计划配置无效：{string.Join("；", validation.Errors)}");
        }

        _plans[planId] = candidatePlan;
        await PersistPlansAsync();
        LogDeletionPolicyChanged(currentPlan, candidatePlan);
        OnPlanUpdated?.Invoke(candidatePlan);
        return candidatePlan;
    }

    public async Task<SyncPlan> ReplacePlanAsync(
        string planId,
        string name,
        string? description,
        string masterNodeId,
        string syncItemType,
        IEnumerable<SyncPlanSlaveConfiguration> slaveConfigurations,
        SyncSchedule schedule,
        bool isEnabled,
        SyncPlanDeletionPolicy? deletionPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(planId);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(masterNodeId);
        ArgumentNullException.ThrowIfNull(syncItemType);
        ArgumentNullException.ThrowIfNull(slaveConfigurations);
        ArgumentNullException.ThrowIfNull(schedule);

        EnsureLoaded();
        var currentPlan = GetRequiredPlan(planId);
        var resolvedMasterNodeId = ResolveMasterNodeId(masterNodeId);

        var candidatePlan = new SyncPlan(
            currentPlan.Id,
            name,
            resolvedMasterNodeId,
            syncItemType,
            slaveConfigurations.ToList(),
            schedule,
            currentPlan.CreatedAt,
            deletionPolicy ?? currentPlan.DeletionPolicy)
        {
            Description = description,
            IsEnabled = isEnabled,
            LastExecutedAt = currentPlan.LastExecutedAt,
            ExecutionCount = currentPlan.ExecutionCount,
            ModifiedAt = DateTimeOffset.Now
        };

        if (candidatePlan.IsEnabled && candidatePlan.Schedule.TriggerType != SyncTriggerType.Manual)
        {
            candidatePlan.Schedule.NextScheduledTime ??= candidatePlan.Schedule.CalculateNextRunTime(DateTimeOffset.Now);
        }
        else if (!candidatePlan.IsEnabled)
        {
            candidatePlan.Schedule.NextScheduledTime = null;
        }

        var validation = await ValidatePlanAsync(candidatePlan);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException($"同步计划配置无效：{string.Join("；", validation.Errors)}");
        }

        _plans[planId] = candidatePlan;
        await PersistPlansAsync();
        LogDeletionPolicyChanged(currentPlan, candidatePlan);
        OnPlanUpdated?.Invoke(candidatePlan);
        return candidatePlan;
    }

    public async Task DeletePlanAsync(string planId)
    {
        ArgumentNullException.ThrowIfNull(planId);

        EnsureLoaded();
        if (_plans.TryRemove(planId, out var removedPlan))
        {
            await PersistPlansAsync();
            OnPlanDeleted?.Invoke(removedPlan);
        }
    }

    public async Task EnablePlanAsync(string planId)
    {
        var plan = GetRequiredPlan(planId);
        plan.IsEnabled = true;
        plan.Schedule.NextScheduledTime ??= plan.Schedule.CalculateNextRunTime(DateTimeOffset.Now);
        plan.Touch();
        await PersistPlansAsync();
        OnPlanStatusChanged?.Invoke(plan, true);
    }

    public async Task DisablePlanAsync(string planId)
    {
        var plan = GetRequiredPlan(planId);
        plan.IsEnabled = false;
        plan.Touch();
        await PersistPlansAsync();
        OnPlanStatusChanged?.Invoke(plan, false);
    }

    public async Task<Dictionary<string, SyncTaskResult>> ExecutePlanNowAsync(string planId, CancellationToken cancellationToken)
    {
        EnsureLoaded();
        var plan = GetRequiredPlan(planId);

        var validation = await ValidatePlanAsync(plan);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException($"同步计划配置无效：{string.Join("；", validation.Errors)}");
        }

        var results = await _syncEngine.ExecutePlanAsync(plan, cancellationToken);
        plan.LastExecutedAt = DateTimeOffset.Now;
        plan.ExecutionCount++;
        plan.Schedule.NextScheduledTime = plan.Schedule.CalculateNextRunTime(plan.LastExecutedAt);
        plan.Touch();
        await PersistPlansAsync();

        _logger.LogInformation("同步计划执行完成。计划={PlanId}，任务数={TaskCount}", plan.Id, results.Count);
        return results;
    }

    public async Task<(bool IsValid, IReadOnlyList<string> Errors)> ValidatePlanAsync(string planId)
    {
        EnsureLoaded();
        var plan = GetRequiredPlan(planId);
        return await ValidatePlanAsync(plan);
    }

    private async Task<(bool IsValid, IReadOnlyList<string> Errors)> ValidatePlanAsync(SyncPlan plan)
    {
        var errors = new List<string>();
        var validation = await _syncTaskGenerator.ValidatePlanAsync(plan);
        errors.AddRange(validation.Errors);
        var resolvedMasterNodeId = ResolveMasterNodeId(plan.MasterNodeId);

        if (plan.Schedule.TriggerType == SyncTriggerType.Scheduled
            && !plan.Schedule.Interval.HasValue
            && string.IsNullOrWhiteSpace(plan.Schedule.CronExpression))
        {
            errors.Add("定时计划至少需要配置 Interval 或 CronExpression。");
        }

        if (!_nodeRegistry.TryGet(resolvedMasterNodeId, out _))
        {
            errors.Add($"未在节点注册表中找到主节点：{resolvedMasterNodeId}。");
        }

        return (errors.Count == 0, errors);
    }

    private static SyncPlan ClonePlan(SyncPlan source)
    {
        var clone = SyncConfigurationMapper.ToSyncPlan(SyncConfigurationMapper.ToSyncPlanOptions(source));
        clone.Description = source.Description;
        clone.IsEnabled = source.IsEnabled;
        clone.ModifiedAt = source.ModifiedAt;
        clone.LastExecutedAt = source.LastExecutedAt;
        clone.ExecutionCount = source.ExecutionCount;
        return clone;
    }

    private static string ResolveMasterNodeId(string masterNodeId)
    {
        return string.IsNullOrWhiteSpace(masterNodeId)
            ? SyncFrameworkOptions.DefaultHostNodeId
            : masterNodeId;
    }

    private void EnsureLoaded()
    {
        var configurationWriteTimeUtc = GetConfigurationWriteTimeUtc();
        if (_isLoaded && configurationWriteTimeUtc <= _lastLoadedConfigurationWriteTimeUtc)
        {
            return;
        }

        var syncOptions = _configurationManagementService.GetSyncOptions();

        _plans.Clear();
        foreach (var planOptions in syncOptions.Plans)
        {
            var plan = SyncConfigurationMapper.ToSyncPlan(planOptions);
            if (string.IsNullOrWhiteSpace(plan.MasterNodeId))
            {
                plan = new SyncPlan(
                    plan.Id,
                    plan.Name,
                    SyncFrameworkOptions.DefaultHostNodeId,
                    plan.SyncItemType,
                    plan.SlaveConfigurations,
                    plan.Schedule,
                    plan.CreatedAt,
                    plan.DeletionPolicy)
                {
                    Description = plan.Description,
                    IsEnabled = plan.IsEnabled,
                    ModifiedAt = plan.ModifiedAt,
                    LastExecutedAt = plan.LastExecutedAt,
                    ExecutionCount = plan.ExecutionCount
                };
            }
            _plans[plan.Id] = plan;
        }

        _isLoaded = true;
        _lastLoadedConfigurationWriteTimeUtc = configurationWriteTimeUtc;
    }

    private SyncPlan GetRequiredPlan(string planId)
    {
        ArgumentNullException.ThrowIfNull(planId);

        EnsureLoaded();
        if (!_plans.TryGetValue(planId, out var plan))
        {
            throw new InvalidOperationException($"未找到同步计划：{planId}");
        }

        return plan;
    }

    private async Task PersistPlansAsync()
    {
        await _planLock.WaitAsync();
        try
        {
            var configuration = await _configurationManagementService.LoadAsync();
            configuration.UniversalSyncService.Sync.Plans = _plans.Values
                .OrderBy(plan => plan.Name, StringComparer.OrdinalIgnoreCase)
                .Select(SyncConfigurationMapper.ToSyncPlanOptions)
                .ToList();
            await _configurationManagementService.SaveAsync(configuration);
            _lastLoadedConfigurationWriteTimeUtc = GetConfigurationWriteTimeUtc();
        }
        finally
        {
            _planLock.Release();
        }
    }

    private DateTimeOffset GetConfigurationWriteTimeUtc()
    {
        var configurationFilePath = _configurationManagementService.ConfigurationFilePath;
        return File.Exists(configurationFilePath)
            ? File.GetLastWriteTimeUtc(configurationFilePath)
            : DateTimeOffset.MinValue;
    }

    private void LogDeletionPolicyInitialized(SyncPlan plan)
    {
        _logger.LogInformation(
            "[AUDIT] 初始化计划删除守卫策略。计划={PlanId} 删除阈值={DeleteThreshold} 百分比阈值={PercentThreshold}% fail-safe={FailSafeMode}",
            plan.Id,
            plan.DeletionPolicy.DeleteThreshold,
            plan.DeletionPolicy.PercentThreshold,
            plan.DeletionPolicy.FailSafeMode);
    }

    private void LogDeletionPolicyChanged(SyncPlan previous, SyncPlan current)
    {
        if (previous.DeletionPolicy.DeleteThreshold == current.DeletionPolicy.DeleteThreshold
            && Math.Abs(previous.DeletionPolicy.PercentThreshold - current.DeletionPolicy.PercentThreshold) < 0.0001d
            && previous.DeletionPolicy.FailSafeMode == current.DeletionPolicy.FailSafeMode
            && previous.DeletionPolicy.AllowThresholdBreachForCurrentRun == current.DeletionPolicy.AllowThresholdBreachForCurrentRun
            && string.Equals(previous.DeletionPolicy.ThresholdOverrideReason, current.DeletionPolicy.ThresholdOverrideReason, StringComparison.Ordinal))
        {
            return;
        }

        _logger.LogWarning(
            "[AUDIT] 计划删除守卫策略变更。计划={PlanId} 删除阈值: {OldDeleteThreshold}->{NewDeleteThreshold} 百分比阈值: {OldPercentThreshold}%->{NewPercentThreshold}% fail-safe: {OldMode}->{NewMode} 本轮越权: {OldOverride}->{NewOverride} 原因: {Reason}",
            current.Id,
            previous.DeletionPolicy.DeleteThreshold,
            current.DeletionPolicy.DeleteThreshold,
            previous.DeletionPolicy.PercentThreshold,
            current.DeletionPolicy.PercentThreshold,
            previous.DeletionPolicy.FailSafeMode,
            current.DeletionPolicy.FailSafeMode,
            previous.DeletionPolicy.AllowThresholdBreachForCurrentRun,
            current.DeletionPolicy.AllowThresholdBreachForCurrentRun,
            current.DeletionPolicy.ThresholdOverrideReason ?? "<none>");
    }
}

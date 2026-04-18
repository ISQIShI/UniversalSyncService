using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UniversalSyncService.Abstractions.Configuration;
using UniversalSyncService.Abstractions.SyncManagement;
using UniversalSyncService.Abstractions.SyncManagement.Plans;
using UniversalSyncService.Core.SyncManagement.ConfigNodes;

namespace UniversalSyncService.Core.SyncManagement;

public sealed class SyncCoordinatorHostedService : BackgroundService
{
    private readonly IConfigurationManagementService _configurationManagementService;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly NodeRegistry _nodeRegistry;
    private readonly ISyncPlanManager _syncPlanManager;
    private readonly ILogger<SyncCoordinatorHostedService> _logger;

    public SyncCoordinatorHostedService(
        IConfigurationManagementService configurationManagementService,
        IHostEnvironment hostEnvironment,
        NodeRegistry nodeRegistry,
        ISyncPlanManager syncPlanManager,
        ILogger<SyncCoordinatorHostedService> logger)
    {
        _configurationManagementService = configurationManagementService;
        _hostEnvironment = hostEnvironment;
        _nodeRegistry = nodeRegistry;
        _syncPlanManager = syncPlanManager;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await LoadConfiguredNodesAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var syncOptions = _configurationManagementService.GetSyncOptions();
            if (!syncOptions.EnableSyncFramework)
            {
                await Task.Delay(TimeSpan.FromSeconds(syncOptions.SchedulerPollingIntervalSeconds), stoppingToken);
                continue;
            }

            foreach (var plan in _syncPlanManager.GetAllPlans().Where(plan => plan.IsEnabled))
            {
                var nextRunTime = ResolveNextRunTime(plan);
                if (nextRunTime.HasValue && nextRunTime <= DateTimeOffset.Now)
                {
                    _logger.LogInformation("检测到到期同步计划，开始执行。计划={PlanId}", plan.Id);
                    try
                    {
                        await _syncPlanManager.ExecutePlanNowAsync(plan.Id, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "执行定时同步计划时发生异常：{PlanId}", plan.Id);
                    }
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(syncOptions.SchedulerPollingIntervalSeconds), stoppingToken);
        }
    }

    private Task LoadConfiguredNodesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var syncOptions = _configurationManagementService.GetSyncOptions();
        _nodeRegistry.Clear();
        RegisterImplicitHostNode(syncOptions);
        foreach (var nodeOptions in syncOptions.Nodes)
        {
            _nodeRegistry.Register(SyncConfigurationMapper.ToNodeConfiguration(nodeOptions));
        }

        _logger.LogInformation("已从配置中加载同步节点。节点数={NodeCount}", syncOptions.Nodes.Count);
        return Task.CompletedTask;
    }

    private void RegisterImplicitHostNode(SyncFrameworkOptions syncOptions)
    {
        var workspaceRoot = Path.GetFullPath(Path.Combine(_hostEnvironment.ContentRootPath, syncOptions.HostWorkspacePath));
        var historyStorePath = Path.GetFullPath(Path.Combine(_hostEnvironment.ContentRootPath, syncOptions.HistoryStorePath));
        var excludedPaths = new[]
        {
            historyStorePath,
            $"{historyStorePath}-wal",
            $"{historyStorePath}-shm",
            $"{historyStorePath}-journal"
        };
        var node = new Abstractions.SyncManagement.ConfigNodes.NodeConfiguration(
            SyncFrameworkOptions.DefaultHostNodeId,
            "宿主本地节点",
            "Local",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["RootPath"] = workspaceRoot,
                ["ExcludedAbsolutePaths"] = string.Join(Path.PathSeparator, excludedPaths)
            })
        {
            IsEnabled = true
        };

        _nodeRegistry.Register(node);
    }

    private static DateTimeOffset? ResolveNextRunTime(SyncPlan plan)
    {
        return plan.Schedule.TriggerType switch
        {
            SyncTriggerType.Manual => null,
            _ => plan.Schedule.NextScheduledTime
                 ?? plan.LastExecutedAt?.Add(plan.Schedule.Interval ?? TimeSpan.FromMinutes(1))
                 ?? plan.CreatedAt.Add(plan.Schedule.Interval ?? TimeSpan.FromMinutes(1))
        };
    }
}

using UniversalSyncService.Abstractions.Configuration;
using UniversalSyncService.Abstractions.Plugins;
using UniversalSyncService.Abstractions.SyncManagement;
using UniversalSyncService.Abstractions.SyncManagement.Engine;

namespace UniversalSyncService.Host;

public sealed class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IConfigurationManagementService _configurationManagementService;
    private readonly IPluginManager _pluginManager;
    private readonly ISyncPlanManager _syncPlanManager;
    private readonly ISyncEngine _syncEngine;

    public Worker(
        ILogger<Worker> logger,
        IConfigurationManagementService configurationManagementService,
        IPluginManager pluginManager,
        ISyncPlanManager syncPlanManager,
        ISyncEngine syncEngine)
    {
        _logger = logger;
        _configurationManagementService = configurationManagementService;
        _pluginManager = pluginManager;
        _syncPlanManager = syncPlanManager;
        _syncEngine = syncEngine;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 持续输出服务心跳，便于确认宿主存活状态与插件加载状态。
        while (!stoppingToken.IsCancellationRequested)
        {
            // 统一通过配置管理器读取运行配置，避免分散读取入口。
            var serviceOptions = _configurationManagementService.GetServiceOptions();
            var heartbeatInterval = TimeSpan.FromSeconds(Math.Max(1, serviceOptions.HeartbeatIntervalSeconds));

            _logger.LogInformation(
                "服务心跳。服务名称={ServiceName}，已加载插件数={PluginCount}，计划数={PlanCount}，活动任务数={ActiveTaskCount}，时间戳={Timestamp}",
                serviceOptions.ServiceName,
                _pluginManager.LoadedPlugins.Count,
                _syncPlanManager.GetAllPlans().Count,
                _syncEngine.ActiveTasks.Count,
                DateTimeOffset.Now);

            try
            {
                await Task.Delay(heartbeatInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Ctrl+C 或宿主关闭时属于正常取消流程，避免向上抛出噪声异常。
                break;
            }
        }

        _logger.LogInformation("后台工作循环已停止。");
    }
}

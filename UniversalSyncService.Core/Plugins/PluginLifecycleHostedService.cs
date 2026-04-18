using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UniversalSyncService.Abstractions.Plugins;

namespace UniversalSyncService.Core.Plugins;

public sealed class PluginLifecycleHostedService : IHostedService
{
    private readonly IPluginManager _pluginManager;
    private readonly ILogger<PluginLifecycleHostedService> _logger;

    public PluginLifecycleHostedService(
        IPluginManager pluginManager,
        ILogger<PluginLifecycleHostedService> logger)
    {
        _pluginManager = pluginManager;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // 在宿主启动阶段加载并初始化全部启用插件。
        _logger.LogInformation("开始初始化插件生命周期管理器。");
        await _pluginManager.InitializeAsync(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // 在宿主停止阶段按顺序停止插件，确保资源被正确释放。
        _logger.LogInformation("开始停止插件生命周期管理器。");
        await _pluginManager.StopAsync(cancellationToken);
    }
}

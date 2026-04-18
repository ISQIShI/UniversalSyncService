using Microsoft.Extensions.DependencyInjection;
using UniversalSyncService.Abstractions.SyncManagement;
using UniversalSyncService.Abstractions.SyncManagement.Engine;
using UniversalSyncService.Abstractions.SyncManagement.History;
using UniversalSyncService.Abstractions.SyncManagement.Tasks;
using UniversalSyncService.Abstractions.Nodes;
using UniversalSyncService.Abstractions.SyncItems;
using UniversalSyncService.Core.Nodes;
using UniversalSyncService.Core.Nodes.OneDrive;
using UniversalSyncService.Core.Providers;
using UniversalSyncService.Abstractions.Plugins;
using UniversalSyncService.Core.Plugins;
using UniversalSyncService.Core.SyncManagement;
using UniversalSyncService.Core.SyncManagement.ConfigNodes;
using UniversalSyncService.Core.SyncManagement.Engine;
using UniversalSyncService.Core.SyncManagement.History;
using UniversalSyncService.Core.SyncItems;
using UniversalSyncService.Core.SyncManagement.Tasks;

namespace UniversalSyncService.Core.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUniversalSyncCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // 插件管理器是全局单例，集中维护已加载插件状态。
        services.AddSingleton<IPluginManager, PluginManager>();

        // 同步框架运行期单例服务。
        services.AddSingleton<NodeRegistry>();
        services.AddSingleton<NodeProviderRegistry>();
        services.AddSingleton<SyncItemFactoryRegistry>();
        services.AddSingleton<INodeProvider, LocalNodeProvider>();
        services.AddSingleton<INodeProvider, OneDriveNodeProvider>();
        services.AddSingleton<OneDriveGraphClientFactory>();
        services.AddSingleton<ISyncItemFactory, FileSystemSyncItemFactory>();
        services.AddSingleton<ISyncHistoryManager, SyncHistoryManager>();
        services.AddSingleton<ISyncAlgorithmEngine, SyncAlgorithmEngine>();
        services.AddSingleton<IConflictResolver, ConflictResolver>();
        services.AddSingleton<ISyncTaskRunner, FileSystemSyncTaskRunner>();
        services.AddSingleton<SyncTaskRunnerRegistry>();
        services.AddSingleton<ISyncTaskGenerator, SyncTaskGenerator>();
        services.AddSingleton<ISyncTaskExecutor, SyncTaskExecutor>();
        services.AddSingleton<ISyncEngine, SyncEngine>();
        services.AddSingleton<ISyncPlanManager, SyncPlanManager>();

        // 通过托管服务接入插件启动与停止生命周期。
        services.AddHostedService<PluginLifecycleHostedService>();
        services.AddHostedService<SyncCoordinatorHostedService>();

        return services;
    }
}

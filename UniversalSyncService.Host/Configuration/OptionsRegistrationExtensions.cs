using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UniversalSyncService.Abstractions.Configuration;

namespace UniversalSyncService.Host.Configuration;

public static class OptionsRegistrationExtensions
{
    public static IServiceCollection AddUniversalSyncOptions(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // 服务运行参数：用于控制服务名称与心跳行为。
        services
            .AddOptions<ServiceOptions>()
            .Bind(configuration.GetSection(ServiceOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.ServiceName), "必须提供服务名称。")
            .Validate(options => options.HeartbeatIntervalSeconds > 0, "心跳间隔必须大于 0 秒。")
            .ValidateOnStart();

        // 接口层参数：统一管理 gRPC、HTTP API 与 Web 控制台开关及访问密钥。
        services
            .AddOptions<InterfaceOptions>()
            .Bind(configuration.GetSection(InterfaceOptions.SectionName))
            .Validate(options => !options.RequireManagementApiKey || !string.IsNullOrWhiteSpace(options.ManagementApiKey), "启用密钥校验时必须提供 Web 管理接口密钥。")
            .ValidateOnStart();

        // 日志参数：控制全局日志级别与输出目标。
        services
            .AddOptions<LoggingOptions>()
            .Bind(configuration.GetSection(LoggingOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.MinimumLevel), "必须提供日志最小级别。")
            .ValidateOnStart();

        // 插件参数：控制插件目录与插件描述符集合。
        services
            .AddOptions<PluginSystemOptions>()
            .Bind(configuration.GetSection(PluginSystemOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.PluginDirectory), "必须提供插件目录。")
            .ValidateOnStart();

        // 同步框架参数：控制计划、节点配置、历史保留与调度轮询。
        services
            .AddOptions<SyncFrameworkOptions>()
            .Bind(configuration.GetSection(SyncFrameworkOptions.SectionName))
            .Validate(options => options.SchedulerPollingIntervalSeconds > 0, "同步轮询间隔必须大于 0 秒。")
            .Validate(options => options.MaxConcurrentTasks > 0, "最大并发任务数必须大于 0。")
            .Validate(options => options.HistoryRetentionVersions > 0, "历史保留版本数必须大于 0。")
            .Validate(options => !string.IsNullOrWhiteSpace(options.HistoryStorePath), "必须提供历史存储路径。")
            .ValidateOnStart();

        // 配置管理服务：提供读取、修改、保存与默认配置生成能力，供未来 UI 调用。
        services.AddSingleton<IConfigurationManagementService, ConfigurationManagementService>();

        return services;
    }
}

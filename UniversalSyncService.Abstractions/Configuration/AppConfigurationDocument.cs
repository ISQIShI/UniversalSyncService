namespace UniversalSyncService.Abstractions.Configuration;

public sealed class AppConfigurationDocument
{
    [ConfigComment("UniversalSyncService 业务配置根节点。")]
    public UniversalSyncServiceConfiguration UniversalSyncService { get; set; } = new();
}

public sealed class UniversalSyncServiceConfiguration
{
    [ConfigComment("服务运行配置。")]
    public ServiceOptions Service { get; set; } = new();

    [ConfigComment("日志配置。")]
    public LoggingOptions Logging { get; set; } = new();

    [ConfigComment("接口层配置。")]
    public InterfaceOptions Interface { get; set; } = new();

    [ConfigComment("插件系统配置。")]
    public PluginSystemOptions Plugins { get; set; } = new();

    [ConfigComment("同步框架配置。")]
    public SyncFrameworkOptions Sync { get; set; } = new();
}

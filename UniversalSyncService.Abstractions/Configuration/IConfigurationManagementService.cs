namespace UniversalSyncService.Abstractions.Configuration;

public interface IConfigurationManagementService
{
    string ConfigurationFilePath { get; }

    string GenerateDefaultYaml();

    ServiceOptions GetServiceOptions();

    LoggingOptions GetLoggingOptions();

    InterfaceOptions GetInterfaceOptions();

    PluginSystemOptions GetPluginSystemOptions();

    SyncFrameworkOptions GetSyncOptions();

    AppConfigurationDocument GetCurrentConfiguration();

    Task EnsureDefaultConfigurationFileAsync(CancellationToken cancellationToken = default);

    Task<AppConfigurationDocument> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AppConfigurationDocument configuration, CancellationToken cancellationToken = default);
}

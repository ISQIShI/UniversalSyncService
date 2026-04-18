namespace UniversalSyncService.Abstractions.Plugins;

public interface IPlugin
{
    PluginMetadata Metadata { get; }

    Task InitializeAsync(CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);
}

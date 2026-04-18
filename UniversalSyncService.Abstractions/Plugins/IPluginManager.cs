namespace UniversalSyncService.Abstractions.Plugins;

public interface IPluginManager
{
    IReadOnlyList<PluginMetadata> LoadedPlugins { get; }

    Task InitializeAsync(CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);
}

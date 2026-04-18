namespace UniversalSyncService.Core.Plugins;

public sealed class PluginDescriptor
{
    public PluginDescriptor(
        string id,
        string assemblyPath,
        string entryType,
        bool enabled,
        string? description)
    {
        Id = id;
        AssemblyPath = assemblyPath;
        EntryType = entryType;
        Enabled = enabled;
        Description = description;
    }

    public string Id { get; }

    public string AssemblyPath { get; }

    public string EntryType { get; }

    public bool Enabled { get; }

    public string? Description { get; }
}

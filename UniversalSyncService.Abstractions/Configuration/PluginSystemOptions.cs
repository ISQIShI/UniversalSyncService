namespace UniversalSyncService.Abstractions.Configuration;

public sealed class PluginSystemOptions
{
    public const string SectionName = "UniversalSyncService:Plugins";

    [ConfigComment("是否启用插件系统。")]
    public bool EnablePluginSystem { get; set; } = true;

    [ConfigComment("插件根目录（支持相对 ContentRoot）。")]
    public string PluginDirectory { get; set; } = "plugins";

    [ConfigComment("插件描述符列表。")]
    public List<PluginDescriptorOptions> Descriptors { get; set; } = [];
}

public sealed class PluginDescriptorOptions
{
    [ConfigComment("插件唯一标识。")]
    public string Id { get; set; } = string.Empty;

    [ConfigComment("是否启用当前插件。")]
    public bool Enabled { get; set; } = true;

    [ConfigComment("插件程序集路径（相对插件目录或绝对路径）。")]
    public string? AssemblyPath { get; set; }

    [ConfigComment("插件入口类型全名。")]
    public string? EntryType { get; set; }

    [ConfigComment("插件说明。")]
    public string? Description { get; set; }
}

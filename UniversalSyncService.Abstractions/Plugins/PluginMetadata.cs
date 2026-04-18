namespace UniversalSyncService.Abstractions.Plugins;

/// <summary>
/// 表示插件元数据。
/// </summary>
public sealed class PluginMetadata
{
    /// <summary>
    /// 获取插件标识。
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// 获取插件版本。
    /// </summary>
    public string Version { get; }

    /// <summary>
    /// 获取插件描述。
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// 初始化 <see cref="PluginMetadata"/> 类的新实例。
    /// </summary>
    public PluginMetadata(string id, string version, string description)
    {
        Id = id;
        Version = version;
        Description = description;
    }
}

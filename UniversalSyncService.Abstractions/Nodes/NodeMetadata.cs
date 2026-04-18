namespace UniversalSyncService.Abstractions.Nodes;

/// <summary>
/// 表示节点类型。
/// </summary>
public enum NodeType
{
    /// <summary>
    /// 本地节点。
    /// </summary>
    Local,

    /// <summary>
    /// 云端节点。
    /// </summary>
    Cloud,

    /// <summary>
    /// 远程节点。
    /// </summary>
    Remote,

    /// <summary>
    /// 设备节点。
    /// </summary>
    Device,

    /// <summary>
    /// 其他类型节点。
    /// </summary>
    Other,
}

/// <summary>
/// 表示节点的不可变元数据。
/// </summary>
public sealed class NodeMetadata
{
    /// <summary>
    /// 获取节点标识。
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// 获取节点名称。
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 获取节点类型。
    /// </summary>
    public NodeType NodeType { get; }

    /// <summary>
    /// 获取节点版本。
    /// </summary>
    public string Version { get; }

    /// <summary>
    /// 获取节点描述。
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// 初始化 <see cref="NodeMetadata"/> 类的新实例。
    /// </summary>
    /// <param name="id">节点标识。</param>
    /// <param name="name">节点名称。</param>
    /// <param name="nodeType">节点类型。</param>
    /// <param name="version">节点版本。</param>
    /// <param name="description">节点描述。</param>
    public NodeMetadata(string id, string name, NodeType nodeType, string version, string description)
    {
        Id = id;
        Name = name;
        NodeType = nodeType;
        Version = version;
        Description = description;
    }
}

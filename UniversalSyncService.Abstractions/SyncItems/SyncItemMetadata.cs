namespace UniversalSyncService.Abstractions.SyncItems;

/// <summary>
/// 表示同步项的不可变元数据。
/// </summary>
public sealed class SyncItemMetadata
{
    /// <summary>
    /// 初始化 <see cref="SyncItemMetadata"/> 的新实例。
    /// </summary>
    public SyncItemMetadata(
        string id,
        string name,
        string path,
        long size,
        DateTimeOffset? createdAt,
        DateTimeOffset? modifiedAt,
        string? checksum,
        string? contentType)
    {
        Id = id;
        Name = name;
        Path = path;
        Size = size;
        CreatedAt = createdAt;
        ModifiedAt = modifiedAt;
        Checksum = checksum;
        ContentType = contentType;
    }

    /// <summary>
    /// 获取唯一标识。
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// 获取名称。
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 获取路径。
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// 获取大小（字节）。
    /// </summary>
    public long Size { get; }

    /// <summary>
    /// 获取创建时间。
    /// </summary>
    public DateTimeOffset? CreatedAt { get; }

    /// <summary>
    /// 获取最后修改时间。
    /// </summary>
    public DateTimeOffset? ModifiedAt { get; }

    /// <summary>
    /// 获取校验值。
    /// </summary>
    public string? Checksum { get; }

    /// <summary>
    /// 获取内容类型。
    /// </summary>
    public string? ContentType { get; }
}

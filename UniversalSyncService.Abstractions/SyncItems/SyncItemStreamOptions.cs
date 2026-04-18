namespace UniversalSyncService.Abstractions.SyncItems;

/// <summary>
/// 表示同步项流操作选项。
/// </summary>
public sealed class SyncItemStreamOptions
{
    /// <summary>
    /// 获取或设置缓冲区大小（字节）。
    /// </summary>
    public int BufferSize { get; set; } = 81920;

    /// <summary>
    /// 获取或设置是否启用压缩。
    /// </summary>
    public bool EnableCompression { get; set; }

    /// <summary>
    /// 获取或设置是否启用加密。
    /// </summary>
    public bool EnableEncryption { get; set; }

    /// <summary>
    /// 获取或设置流偏移量。
    /// </summary>
    public long Offset { get; set; }

    /// <summary>
    /// 获取或设置流长度。
    /// </summary>
    public long? Length { get; set; }
}

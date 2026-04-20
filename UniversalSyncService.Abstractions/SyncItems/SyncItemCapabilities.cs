namespace UniversalSyncService.Abstractions.SyncItems;

/// <summary>
/// 表示同步项能力。
/// </summary>
[Flags]
public enum SyncItemCapabilities
{
    /// <summary>
    /// 不具备能力。
    /// </summary>
    None = 0,

    /// <summary>
    /// 可读取内容流。
    /// </summary>
    CanReadContent = 1,

    /// <summary>
    /// 可写入内容流。
    /// </summary>
    CanWriteContent = 2,

    /// <summary>
    /// 可枚举子项。
    /// </summary>
    CanEnumerateChildren = 4,

    /// <summary>
    /// 可提供校验和。
    /// </summary>
    CanComputeChecksum = 8,

    /// <summary>
    /// 可提供扩展元数据。
    /// </summary>
    CanProvideExtendedMetadata = 16,
}

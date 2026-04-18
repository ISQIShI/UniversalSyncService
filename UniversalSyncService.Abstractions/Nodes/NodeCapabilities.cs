namespace UniversalSyncService.Abstractions.Nodes;

/// <summary>
/// 表示节点能力。
/// </summary>
[Flags]
public enum NodeCapabilities
{
    /// <summary>
    /// 未启用任何能力。
    /// </summary>
    None = 0,

    /// <summary>
    /// 支持读取。
    /// </summary>
    CanRead = 1,

    /// <summary>
    /// 支持写入。
    /// </summary>
    CanWrite = 2,

    /// <summary>
    /// 支持删除。
    /// </summary>
    CanDelete = 4,

    /// <summary>
    /// 支持流式传输。
    /// </summary>
    CanStream = 8,

    /// <summary>
    /// 支持断点续传。
    /// </summary>
    SupportsResume = 16,
}

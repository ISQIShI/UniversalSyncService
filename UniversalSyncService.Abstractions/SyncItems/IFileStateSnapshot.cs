namespace UniversalSyncService.Abstractions.SyncItems;

/// <summary>
/// 表示文件状态快照。
/// </summary>
public interface IFileStateSnapshot
{
    /// <summary>
    /// 获取文件路径。
    /// </summary>
    string Path { get; }

    /// <summary>
    /// 获取文件大小。
    /// </summary>
    long Size { get; }

    /// <summary>
    /// 获取最后修改时间。
    /// </summary>
    DateTimeOffset? ModifiedAt { get; }

    /// <summary>
    /// 获取校验值。
    /// </summary>
    string? Checksum { get; }
}

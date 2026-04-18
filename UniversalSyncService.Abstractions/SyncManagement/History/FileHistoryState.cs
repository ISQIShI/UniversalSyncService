namespace UniversalSyncService.Abstractions.SyncManagement.History;

/// <summary>
/// 表示文件历史状态。
/// </summary>
public enum FileHistoryState
{
    /// <summary>
    /// 文件存在。
    /// </summary>
    Exists,

    /// <summary>
    /// 文件已删除。
    /// </summary>
    Deleted,
}

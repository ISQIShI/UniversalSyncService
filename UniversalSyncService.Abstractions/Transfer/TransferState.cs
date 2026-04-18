namespace UniversalSyncService.Abstractions.Transfer;

/// <summary>
/// 表示传输状态。
/// </summary>
public enum TransferState
{
    /// <summary>
    /// 等待开始。
    /// </summary>
    Pending,

    /// <summary>
    /// 正在执行。
    /// </summary>
    Running,

    /// <summary>
    /// 已暂停。
    /// </summary>
    Paused,

    /// <summary>
    /// 已完成。
    /// </summary>
    Completed,

    /// <summary>
    /// 已失败。
    /// </summary>
    Failed,

    /// <summary>
    /// 已取消。
    /// </summary>
    Cancelled,
}

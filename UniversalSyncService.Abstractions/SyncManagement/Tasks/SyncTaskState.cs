using UniversalSyncService.Abstractions.SyncManagement.Plans;

namespace UniversalSyncService.Abstractions.SyncManagement.Tasks;

/// <summary>
/// 表示同步任务状态。
/// </summary>
public enum SyncTaskState
{
    /// <summary>
    /// 等待执行。
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
    /// 失败。
    /// </summary>
    Failed,

    /// <summary>
    /// 已取消。
    /// </summary>
    Cancelled,

    /// <summary>
    /// 冲突需要解决。
    /// </summary>
    Conflict,
}

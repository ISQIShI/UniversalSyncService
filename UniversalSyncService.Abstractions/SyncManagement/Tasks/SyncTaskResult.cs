namespace UniversalSyncService.Abstractions.SyncManagement.Tasks;

/// <summary>
/// 表示同步任务的执行结果。
/// </summary>
public enum SyncTaskResult
{
    /// <summary>
    /// 成功完成。
    /// </summary>
    Success,

    /// <summary>
    /// 部分成功（有一些文件同步失败但任务继续）。
    /// </summary>
    PartialSuccess,

    /// <summary>
    /// 失败。
    /// </summary>
    Failed,

    /// <summary>
    /// 已取消。
    /// </summary>
    Cancelled,

    /// <summary>
    /// 发生冲突需要用户干预。
    /// </summary>
    Conflict,

    /// <summary>
    /// 无变化（两边已经同步）。
    /// </summary>
    NoChanges,
}

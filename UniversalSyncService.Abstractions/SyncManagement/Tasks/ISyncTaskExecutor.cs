namespace UniversalSyncService.Abstractions.SyncManagement.Tasks;

/// <summary>
/// 表示同步任务执行器。
/// 负责同步任务的执行、并发控制、队列管理和生命周期事件通知。
/// </summary>
public interface ISyncTaskExecutor
{
    /// <summary>
    /// 获取或设置允许同时执行的最大任务数。
    /// </summary>
    int MaxConcurrentTasks { get; set; }

    /// <summary>
    /// 获取当前是否处于暂停状态。
    /// </summary>
    bool IsPaused { get; }

    /// <summary>
    /// 获取当前排队等待执行的任务数量。
    /// </summary>
    int QueuedTaskCount { get; }

    /// <summary>
    /// 当任务开始执行时触发。
    /// </summary>
    event Action<ISyncTask>? OnTaskStarted;

    /// <summary>
    /// 当任务完成时触发。
    /// </summary>
    event Action<ISyncTask, SyncTaskResult>? OnTaskCompleted;

    /// <summary>
    /// 当任务执行失败时触发。
    /// </summary>
    event Action<ISyncTask, Exception>? OnTaskFailed;

    /// <summary>
    /// 当任务进度发生变化时触发。
    /// </summary>
    event Action<ISyncTask, ISyncTaskProgress>? OnTaskProgressChanged;

    /// <summary>
    /// 执行单个同步任务。
    /// </summary>
    /// <param name="task">同步任务。</param>
    /// <param name="progress">进度报告器。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>任务执行结果。</returns>
    Task<SyncTaskResult> ExecuteAsync(
        ISyncTask task,
        IProgress<ISyncTaskProgress>? progress,
        CancellationToken cancellationToken);

    /// <summary>
    /// 批量执行同步任务。
    /// </summary>
    /// <param name="tasks">待执行的任务集合。</param>
    /// <param name="options">批量执行选项。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>每个任务的执行结果列表。</returns>
    Task<IReadOnlyList<SyncTaskResult>> ExecuteBatchAsync(
        IEnumerable<ISyncTask> tasks,
        SyncTaskExecutionOptions options,
        CancellationToken cancellationToken);

    /// <summary>
    /// 将任务加入执行队列。
    /// </summary>
    /// <param name="task">待入队的任务。</param>
    /// <returns>表示入队操作的任务。</returns>
    Task QueueTaskAsync(ISyncTask task);

    /// <summary>
    /// 暂停执行。
    /// </summary>
    /// <returns>表示暂停操作的任务。</returns>
    Task PauseExecution();

    /// <summary>
    /// 恢复执行。
    /// </summary>
    /// <returns>表示恢复操作的任务。</returns>
    Task ResumeExecution();
}

/// <summary>
/// 表示同步任务批量执行选项。
/// </summary>
public sealed class SyncTaskExecutionOptions
{
    /// <summary>
    /// 获取或设置批量执行时允许的最大并发任务数。
    /// </summary>
    public int? MaxConcurrentTasks { get; set; }

    /// <summary>
    /// 获取或设置是否允许将超出并发限制的任务加入队列。
    /// </summary>
    public bool AllowQueueing { get; set; } = true;
}

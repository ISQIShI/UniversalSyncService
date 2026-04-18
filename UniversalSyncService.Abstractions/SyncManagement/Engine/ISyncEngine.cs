using UniversalSyncService.Abstractions.SyncManagement.Plans;
using UniversalSyncService.Abstractions.SyncManagement.Tasks;

namespace UniversalSyncService.Abstractions.SyncManagement.Engine;

/// <summary>
/// 表示同步引擎。
/// 协调同步计划、任务生成、任务执行和同步算法的核心组件。
/// </summary>
public interface ISyncEngine
{
    /// <summary>
    /// 获取当前正在执行的任务列表。
    /// </summary>
    IReadOnlyList<ISyncTask> ActiveTasks { get; }

    /// <summary>
    /// 执行同步计划。
    /// 将计划解析为任务并逐个执行。
    /// </summary>
    /// <param name="plan">同步计划。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>每个任务的执行结果。</returns>
    Task<Dictionary<string, SyncTaskResult>> ExecutePlanAsync(SyncPlan plan, CancellationToken cancellationToken);

    /// <summary>
    /// 执行单个同步任务。
    /// </summary>
    /// <param name="task">同步任务。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>执行结果。</returns>
    Task<SyncTaskResult> ExecuteTaskAsync(ISyncTask task, CancellationToken cancellationToken);

    /// <summary>
    /// 取消正在执行的计划。
    /// </summary>
    /// <param name="planId">计划ID。</param>
    Task CancelPlanAsync(string planId);

    /// <summary>
    /// 取消特定的任务。
    /// </summary>
    /// <param name="taskId">任务ID。</param>
    Task CancelTaskAsync(string taskId);

    /// <summary>
    /// 当任务开始执行时触发。
    /// </summary>
    event Action<ISyncTask>? OnTaskStarted;

    /// <summary>
    /// 当任务进度更新时触发。
    /// </summary>
    event Action<ISyncTask, ISyncTaskProgress>? OnTaskProgressChanged;

    /// <summary>
    /// 当任务完成时触发。
    /// </summary>
    event Action<ISyncTask, SyncTaskResult>? OnTaskCompleted;

    /// <summary>
    /// 当检测到冲突时触发。
    /// </summary>
    event Action<ISyncTask, ISyncConflict>? OnConflictDetected;
}

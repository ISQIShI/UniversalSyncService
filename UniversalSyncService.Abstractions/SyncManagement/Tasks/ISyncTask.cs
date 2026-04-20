using UniversalSyncService.Abstractions.SyncManagement.ConfigNodes;
using UniversalSyncService.Abstractions.SyncManagement.Engine;
using UniversalSyncService.Abstractions.SyncManagement.Plans;

namespace UniversalSyncService.Abstractions.SyncManagement.Tasks;

/// <summary>
/// 表示同步任务。
/// 由同步计划解析生成，表示主节点与一个从节点之间的具体同步操作。
/// </summary>
public interface ISyncTask
{
    /// <summary>
    /// 获取任务的唯一标识符。
    /// </summary>
    string Id { get; }

    /// <summary>
    /// 获取所属计划的ID。
    /// </summary>
    string PlanId { get; }

    /// <summary>
    /// 获取主节点配置（本地节点）。
    /// </summary>
    NodeConfiguration MasterNode { get; }

    /// <summary>
    /// 获取从节点配置（远程节点）。
    /// </summary>
    NodeConfiguration SlaveNode { get; }

    /// <summary>
    /// 获取同步模式。
    /// </summary>
    SyncMode SyncMode { get; }

    /// <summary>
    /// 获取同步对象类型。
    /// </summary>
    string SyncItemType { get; }

    /// <summary>
    /// 获取当前任务在从节点侧使用的源路径。
    /// </summary>
    string? SourcePath { get; }

    /// <summary>
    /// 获取当前任务在主节点侧使用的目标路径。
    /// </summary>
    string? TargetPath { get; }

    /// <summary>
    /// 获取任务的冲突解决策略。
    /// </summary>
    ConflictResolutionStrategy ConflictResolutionStrategy { get; }

    /// <summary>
    /// 获取任务对应计划的删除守卫策略快照。
    /// </summary>
    SyncPlanDeletionPolicy DeletionPolicy { get; }

    /// <summary>
    /// 获取任务的当前状态。
    /// </summary>
    SyncTaskState State { get; }

    /// <summary>
    /// 获取任务的执行结果。
    /// </summary>
    SyncTaskResult? Result { get; }

    /// <summary>
    /// 获取任务进度。
    /// </summary>
    ISyncTaskProgress? Progress { get; }

    /// <summary>
    /// 获取任务开始时间。
    /// </summary>
    DateTimeOffset? StartTime { get; }

    /// <summary>
    /// 获取任务完成时间。
    /// </summary>
    DateTimeOffset? CompletionTime { get; }

    /// <summary>
    /// 获取执行过程中的错误信息（如果有）。
    /// </summary>
    IReadOnlyList<string>? Errors { get; }

    /// <summary>
    /// 当任务状态改变时触发。
    /// </summary>
    event Action<ISyncTask, SyncTaskState>? OnStateChanged;

    /// <summary>
    /// 当任务进度更新时触发。
    /// </summary>
    event Action<ISyncTask, ISyncTaskProgress>? OnProgressChanged;

    /// <summary>
    /// 当任务完成时触发。
    /// </summary>
    event Action<ISyncTask, SyncTaskResult>? OnCompleted;

    /// <summary>
    /// 由具体运行器上报任务进度。
    /// </summary>
    void ReportProgress(
        int processedFiles,
        int totalFiles,
        long transferredBytes,
        long totalBytes,
        string? currentFilePath,
        string currentOperation);

    /// <summary>
    /// 由具体运行器补充执行错误。
    /// </summary>
    void AddError(string error);

    /// <summary>
    /// 执行同步任务。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>执行结果。</returns>
    Task<SyncTaskResult> ExecuteAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 暂停任务执行。
    /// </summary>
    Task PauseAsync();

    /// <summary>
    /// 恢复任务执行。
    /// </summary>
    Task ResumeAsync();

    /// <summary>
    /// 取消任务执行。
    /// </summary>
    Task CancelAsync();
}

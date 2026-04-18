using Microsoft.Extensions.Logging;
using UniversalSyncService.Abstractions.SyncManagement.ConfigNodes;
using UniversalSyncService.Abstractions.SyncManagement.Engine;
using UniversalSyncService.Abstractions.SyncManagement.Plans;
using UniversalSyncService.Abstractions.SyncManagement.Tasks;

namespace UniversalSyncService.Core.SyncManagement.Tasks;

public sealed class SyncTask : ISyncTask
{
    private readonly ILogger<SyncTask> _logger;
    private readonly List<string> _errors = [];
    private readonly TaskExecutionRequirement _executionRequirement;
    private readonly Func<ISyncTask, CancellationToken, Task<SyncTaskResult>> _executeCoreAsync;
    private bool _isCancelled;

    public SyncTask(
        string id,
        string planId,
        NodeConfiguration masterNode,
        NodeConfiguration slaveNode,
        SyncMode syncMode,
        string syncItemType,
        string? sourcePath,
        string? targetPath,
        ConflictResolutionStrategy conflictResolutionStrategy,
        TaskExecutionRequirement executionRequirement,
        Func<ISyncTask, CancellationToken, Task<SyncTaskResult>> executeCoreAsync,
        ILogger<SyncTask> logger)
    {
        Id = id;
        PlanId = planId;
        MasterNode = masterNode;
        SlaveNode = slaveNode;
        SyncMode = syncMode;
        SyncItemType = syncItemType;
        SourcePath = sourcePath;
        TargetPath = targetPath;
        ConflictResolutionStrategy = conflictResolutionStrategy;
        _executionRequirement = executionRequirement;
        _executeCoreAsync = executeCoreAsync;
        _logger = logger;
        State = SyncTaskState.Pending;
    }

    public string Id { get; }

    public string PlanId { get; }

    public NodeConfiguration MasterNode { get; }

    public NodeConfiguration SlaveNode { get; }

    public SyncMode SyncMode { get; }

    public string SyncItemType { get; }

    public string? SourcePath { get; }

    public string? TargetPath { get; }

    public ConflictResolutionStrategy ConflictResolutionStrategy { get; }

    public SyncTaskState State { get; private set; }

    public SyncTaskResult? Result { get; private set; }

    public ISyncTaskProgress? Progress { get; private set; }

    public DateTimeOffset? StartTime { get; private set; }

    public DateTimeOffset? CompletionTime { get; private set; }

    public IReadOnlyList<string>? Errors => _errors;

    public event Action<ISyncTask, SyncTaskState>? OnStateChanged;

    public event Action<ISyncTask, ISyncTaskProgress>? OnProgressChanged;

    public event Action<ISyncTask, SyncTaskResult>? OnCompleted;

    public Task PauseAsync()
    {
        if (State == SyncTaskState.Running)
        {
            UpdateState(SyncTaskState.Paused);
        }

        return Task.CompletedTask;
    }

    public Task ResumeAsync()
    {
        if (State == SyncTaskState.Paused)
        {
            UpdateState(SyncTaskState.Running);
        }

        return Task.CompletedTask;
    }

    public Task CancelAsync()
    {
        _isCancelled = true;
        if (State is SyncTaskState.Pending or SyncTaskState.Running or SyncTaskState.Paused)
        {
            Complete(SyncTaskState.Cancelled, SyncTaskResult.Cancelled);
        }

        return Task.CompletedTask;
    }

    public Task<SyncTaskResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_isCancelled)
        {
            Complete(SyncTaskState.Cancelled, SyncTaskResult.Cancelled);
            return Task.FromResult(SyncTaskResult.Cancelled);
        }

        StartTime = DateTimeOffset.Now;
        UpdateState(SyncTaskState.Running);

        var progress = new SyncTaskProgress(StartTime.Value, "准备执行同步任务");
        progress.Update(0, 0, 0, 0, null, "同步框架已到达任务层，等待具体节点/同步对象实现接入");
        Progress = progress;
        OnProgressChanged?.Invoke(this, progress);

        if (!MasterNode.IsEnabled || !SlaveNode.IsEnabled)
        {
            _errors.Add("主节点或从节点已被禁用，任务无法执行。");
            _logger.LogWarning("同步任务执行失败：节点已禁用。任务={TaskId}", Id);
            Complete(SyncTaskState.Failed, SyncTaskResult.Failed);
            return Task.FromResult(SyncTaskResult.Failed);
        }

        var missingReason = _executionRequirement switch
        {
            TaskExecutionRequirement.MissingNodeImplementation => "尚未接入节点特化实现，任务当前只能完成框架级编排。",
            TaskExecutionRequirement.MissingSyncItemImplementation => "尚未接入同步对象特化实现，任务当前无法进入实际传输阶段。",
            _ => string.Empty
        };

        if (!string.IsNullOrWhiteSpace(missingReason))
        {
            _errors.Add(missingReason);
            _logger.LogInformation("同步任务已到达框架边界。任务={TaskId}，原因={Reason}", Id, missingReason);
            Complete(SyncTaskState.Failed, SyncTaskResult.Failed);
            return Task.FromResult(SyncTaskResult.Failed);
        }

        return ExecuteCoreInternalAsync(cancellationToken);
    }

    public void ReportProgress(
        int processedFiles,
        int totalFiles,
        long transferredBytes,
        long totalBytes,
        string? currentFilePath,
        string currentOperation)
    {
        var progress = Progress as SyncTaskProgress ?? new SyncTaskProgress(StartTime ?? DateTimeOffset.Now, currentOperation);
        progress.Update(processedFiles, totalFiles, transferredBytes, totalBytes, currentFilePath, currentOperation);
        Progress = progress;
        OnProgressChanged?.Invoke(this, progress);
    }

    public void AddError(string error)
    {
        if (!string.IsNullOrWhiteSpace(error))
        {
            _errors.Add(error);
        }
    }

    private void UpdateState(SyncTaskState state)
    {
        State = state;
        OnStateChanged?.Invoke(this, state);
    }

    private void Complete(SyncTaskState state, SyncTaskResult result)
    {
        Result = result;
        CompletionTime = DateTimeOffset.Now;
        UpdateState(state);
        OnCompleted?.Invoke(this, result);
    }

    private async Task<SyncTaskResult> ExecuteCoreInternalAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _executeCoreAsync(this, cancellationToken);
            Complete(MapState(result), result);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Complete(SyncTaskState.Cancelled, SyncTaskResult.Cancelled);
            return SyncTaskResult.Cancelled;
        }
        catch (Exception ex)
        {
            AddError(ex.Message);
            _logger.LogError(ex, "同步任务执行异常。任务={TaskId}", Id);
            Complete(SyncTaskState.Failed, SyncTaskResult.Failed);
            return SyncTaskResult.Failed;
        }
    }

    private static SyncTaskState MapState(SyncTaskResult result)
    {
        return result switch
        {
            SyncTaskResult.Success or SyncTaskResult.NoChanges or SyncTaskResult.PartialSuccess => SyncTaskState.Completed,
            SyncTaskResult.Cancelled => SyncTaskState.Cancelled,
            SyncTaskResult.Conflict => SyncTaskState.Conflict,
            _ => SyncTaskState.Failed
        };
    }
}

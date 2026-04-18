using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using UniversalSyncService.Abstractions.SyncManagement.Engine;
using UniversalSyncService.Abstractions.SyncManagement.Plans;
using UniversalSyncService.Abstractions.SyncManagement.Tasks;

namespace UniversalSyncService.Core.SyncManagement.Engine;

public sealed class SyncEngine : ISyncEngine
{
    private readonly ISyncTaskGenerator _taskGenerator;
    private readonly ISyncTaskExecutor _taskExecutor;
    private readonly ILogger<SyncEngine> _logger;
    private readonly ConcurrentDictionary<string, ISyncTask> _activeTasks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _taskCancellationSources = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, List<string>> _planTaskMap = new(StringComparer.OrdinalIgnoreCase);

    public SyncEngine(
        ISyncTaskGenerator taskGenerator,
        ISyncTaskExecutor taskExecutor,
        ILogger<SyncEngine> logger)
    {
        _taskGenerator = taskGenerator;
        _taskExecutor = taskExecutor;
        _logger = logger;

        _taskExecutor.OnTaskStarted += task => OnTaskStarted?.Invoke(task);
        _taskExecutor.OnTaskCompleted += HandleTaskCompleted;
        _taskExecutor.OnTaskProgressChanged += (task, progress) => OnTaskProgressChanged?.Invoke(task, progress);
    }

    public IReadOnlyList<ISyncTask> ActiveTasks => _activeTasks.Values.ToList();

    public event Action<ISyncTask>? OnTaskStarted;

    public event Action<ISyncTask, ISyncTaskProgress>? OnTaskProgressChanged;

    public event Action<ISyncTask, SyncTaskResult>? OnTaskCompleted;

    public event Action<ISyncTask, ISyncConflict>? OnConflictDetected;

    public async Task<Dictionary<string, SyncTaskResult>> ExecutePlanAsync(SyncPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var tasks = await _taskGenerator.GenerateTasksAsync(plan);
        var taskIds = tasks.Select(task => task.Id).ToList();
        _planTaskMap[plan.Id] = taskIds;

        var options = new SyncTaskExecutionOptions();
        var actualConcurrency = Math.Max(1, options.MaxConcurrentTasks ?? _taskExecutor.MaxConcurrentTasks);
        using var semaphore = new SemaphoreSlim(actualConcurrency, actualConcurrency);

        var executionResults = new ConcurrentDictionary<string, SyncTaskResult>(StringComparer.OrdinalIgnoreCase);
        var executions = tasks.Select(async task =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                executionResults[task.Id] = await ExecuteTaskAsync(task, cancellationToken);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(executions);

        var results = new Dictionary<string, SyncTaskResult>(StringComparer.OrdinalIgnoreCase);
        foreach (var task in tasks)
        {
            results[task.Id] = executionResults.TryGetValue(task.Id, out var result)
                ? result
                : SyncTaskResult.Failed;
        }

        return results;
    }

    public async Task<SyncTaskResult> ExecuteTaskAsync(ISyncTask task, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(task);

        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _taskCancellationSources[task.Id] = linkedCts;
        _activeTasks[task.Id] = task;

        _logger.LogInformation("开始执行同步任务：{TaskId}", task.Id);
        try
        {
            return await _taskExecutor.ExecuteAsync(task, progress: null, linkedCts.Token);
        }
        finally
        {
            _taskCancellationSources.TryRemove(task.Id, out var removedCts);
            removedCts?.Dispose();
        }
    }

    public Task CancelPlanAsync(string planId)
    {
        ArgumentNullException.ThrowIfNull(planId);

        if (_planTaskMap.TryGetValue(planId, out var taskIds))
        {
            foreach (var taskId in taskIds)
            {
                if (_taskCancellationSources.TryGetValue(taskId, out var cancellationSource))
                {
                    cancellationSource.Cancel();
                }
            }
        }

        return Task.CompletedTask;
    }

    public Task CancelTaskAsync(string taskId)
    {
        ArgumentNullException.ThrowIfNull(taskId);

        if (_taskCancellationSources.TryGetValue(taskId, out var cancellationSource))
        {
            cancellationSource.Cancel();
        }

        return Task.CompletedTask;
    }

    private void HandleTaskCompleted(ISyncTask task, SyncTaskResult result)
    {
        _activeTasks.TryRemove(task.Id, out _);
        _taskCancellationSources.TryRemove(task.Id, out var removedCts);
        removedCts?.Dispose();
        OnTaskCompleted?.Invoke(task, result);

        if (result == SyncTaskResult.Conflict)
        {
            var conflict = new SyncConflict(
                $"plan:{task.PlanId}/task:{task.Id}",
                null,
                null,
                null,
                null,
                "任务执行阶段报告了冲突。") ;
            OnConflictDetected?.Invoke(task, conflict);
        }
    }
}

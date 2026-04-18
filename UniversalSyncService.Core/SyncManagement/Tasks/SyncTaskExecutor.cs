using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using UniversalSyncService.Abstractions.Configuration;
using UniversalSyncService.Abstractions.SyncManagement.Tasks;

namespace UniversalSyncService.Core.SyncManagement.Tasks;

public sealed class SyncTaskExecutor : ISyncTaskExecutor
{
    private readonly IConfigurationManagementService _configurationManagementService;
    private readonly ILogger<SyncTaskExecutor> _logger;
    private readonly ConcurrentQueue<ISyncTask> _queue = new();
    private volatile bool _isPaused;

    public SyncTaskExecutor(
        IConfigurationManagementService configurationManagementService,
        ILogger<SyncTaskExecutor> logger)
    {
        _configurationManagementService = configurationManagementService;
        _logger = logger;
        MaxConcurrentTasks = Math.Max(1, _configurationManagementService.GetSyncOptions().MaxConcurrentTasks);
    }

    public int MaxConcurrentTasks { get; set; }

    public bool IsPaused => _isPaused;

    public int QueuedTaskCount => _queue.Count;

    public event Action<ISyncTask>? OnTaskStarted;

    public event Action<ISyncTask, SyncTaskResult>? OnTaskCompleted;

    public event Action<ISyncTask, Exception>? OnTaskFailed;

    public event Action<ISyncTask, ISyncTaskProgress>? OnTaskProgressChanged;

    public async Task<SyncTaskResult> ExecuteAsync(
        ISyncTask task,
        IProgress<ISyncTaskProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(task);

        await WaitWhilePausedAsync(cancellationToken);
        try
        {
            Subscribe(task, progress);
            OnTaskStarted?.Invoke(task);
            var result = await task.ExecuteAsync(cancellationToken);
            OnTaskCompleted?.Invoke(task, result);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            OnTaskCompleted?.Invoke(task, SyncTaskResult.Cancelled);
            return SyncTaskResult.Cancelled;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "同步任务执行失败：{TaskId}", task.Id);
            OnTaskFailed?.Invoke(task, ex);
            return SyncTaskResult.Failed;
        }
    }

    public async Task<IReadOnlyList<SyncTaskResult>> ExecuteBatchAsync(
        IEnumerable<ISyncTask> tasks,
        SyncTaskExecutionOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(options);

        var taskList = tasks.ToList();
        var actualConcurrency = Math.Max(1, options.MaxConcurrentTasks ?? MaxConcurrentTasks);
        var results = new ConcurrentDictionary<int, SyncTaskResult>();

        using var batchSemaphore = new SemaphoreSlim(actualConcurrency, actualConcurrency);
        var executions = taskList.Select(async (task, index) =>
        {
            if (options.AllowQueueing)
            {
                _queue.Enqueue(task);
            }

            await batchSemaphore.WaitAsync(cancellationToken);
            try
            {
                var result = await ExecuteAsync(task, progress: null, cancellationToken);
                results[index] = result;
            }
            finally
            {
                if (options.AllowQueueing)
                {
                    _queue.TryDequeue(out _);
                }

                batchSemaphore.Release();
            }
        });

        await Task.WhenAll(executions);

        return Enumerable.Range(0, taskList.Count)
            .Select(index => results.TryGetValue(index, out var result) ? result : SyncTaskResult.Failed)
            .ToList();
    }

    public Task QueueTaskAsync(ISyncTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        _queue.Enqueue(task);
        return Task.CompletedTask;
    }

    public Task PauseExecution()
    {
        _isPaused = true;
        return Task.CompletedTask;
    }

    public Task ResumeExecution()
    {
        _isPaused = false;
        return Task.CompletedTask;
    }

    private void Subscribe(ISyncTask task, IProgress<ISyncTaskProgress>? progress)
    {
        task.OnProgressChanged += HandleProgressChanged;

        void HandleProgressChanged(ISyncTask sourceTask, ISyncTaskProgress sourceProgress)
        {
            progress?.Report(sourceProgress);
            OnTaskProgressChanged?.Invoke(sourceTask, sourceProgress);
        }
    }

    private async Task WaitWhilePausedAsync(CancellationToken cancellationToken)
    {
        while (_isPaused)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
        }
    }
}

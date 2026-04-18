using UniversalSyncService.Abstractions.SyncManagement.ConfigNodes;
using UniversalSyncService.Abstractions.SyncManagement.Plans;
using UniversalSyncService.Abstractions.SyncManagement.Tasks;

namespace UniversalSyncService.Core.SyncManagement.Tasks;

/// <summary>
/// 统一管理同步任务运行器，避免任务生成层直接绑定具体实现。
/// </summary>
public sealed class SyncTaskRunnerRegistry
{
    private readonly IReadOnlyList<ISyncTaskRunner> _runners;

    public SyncTaskRunnerRegistry(IEnumerable<ISyncTaskRunner> runners)
    {
        ArgumentNullException.ThrowIfNull(runners);
        _runners = runners.ToList();
    }

    public TaskExecutionRequirement GetExecutionRequirement(
        string syncItemType,
        NodeConfiguration masterNode,
        NodeConfiguration slaveNode,
        SyncPlanSlaveConfiguration slaveConfiguration)
    {
        ArgumentNullException.ThrowIfNull(masterNode);
        ArgumentNullException.ThrowIfNull(slaveNode);
        ArgumentNullException.ThrowIfNull(slaveConfiguration);

        var runner = ResolveRunner(syncItemType);
        return runner?.GetExecutionRequirement(syncItemType, masterNode, slaveNode, slaveConfiguration)
            ?? TaskExecutionRequirement.MissingSyncItemImplementation;
    }

    public Task<SyncTaskResult> ExecuteAsync(ISyncTask task, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(task);

        var runner = ResolveRunner(task.SyncItemType)
            ?? throw new InvalidOperationException($"未找到可用于同步对象类型 {task.SyncItemType} 的任务运行器。");

        return runner.ExecuteAsync(task, cancellationToken);
    }

    private ISyncTaskRunner? ResolveRunner(string syncItemType)
    {
        if (string.IsNullOrWhiteSpace(syncItemType))
        {
            return null;
        }

        return _runners.FirstOrDefault(candidate => candidate.CanRun(syncItemType));
    }
}

using UniversalSyncService.Abstractions.SyncManagement.ConfigNodes;
using UniversalSyncService.Abstractions.SyncManagement.Plans;

namespace UniversalSyncService.Abstractions.SyncManagement.Tasks;

/// <summary>
/// 表示同步任务运行器。
/// 负责判断某类同步任务是否可执行，并承接具体执行逻辑。
/// </summary>
public interface ISyncTaskRunner
{
    /// <summary>
    /// 判断当前运行器是否支持指定同步对象类型。
    /// </summary>
    bool CanRun(string syncItemType);

    /// <summary>
    /// 评估给定计划片段是否已具备执行条件。
    /// </summary>
    TaskExecutionRequirement GetExecutionRequirement(
        string syncItemType,
        NodeConfiguration masterNode,
        NodeConfiguration slaveNode,
        SyncPlanSlaveConfiguration slaveConfiguration);

    /// <summary>
    /// 执行同步任务。
    /// </summary>
    Task<SyncTaskResult> ExecuteAsync(ISyncTask task, CancellationToken cancellationToken);
}

namespace UniversalSyncService.Abstractions.SyncManagement.Tasks;

/// <summary>
/// 表示当前任务是否已具备执行条件。
/// </summary>
public enum TaskExecutionRequirement
{
    /// <summary>
    /// 已具备执行条件。
    /// </summary>
    Ready,

    /// <summary>
    /// 缺少节点特化实现。
    /// </summary>
    MissingNodeImplementation,

    /// <summary>
    /// 缺少同步对象特化实现。
    /// </summary>
    MissingSyncItemImplementation,
}

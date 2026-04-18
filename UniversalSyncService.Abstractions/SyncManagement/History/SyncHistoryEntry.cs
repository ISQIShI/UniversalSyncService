using UniversalSyncService.Abstractions.SyncItems;

namespace UniversalSyncService.Abstractions.SyncManagement.History;

/// <summary>
/// 表示同步历史记录条目。
/// 用于存储每次成功同步后的文件状态，支持remotely-save的同步算法v3。
/// </summary>
public sealed class SyncHistoryEntry
{
    /// <summary>
    /// 获取条目的唯一标识符。
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// 获取关联的计划ID。
    /// </summary>
    public string PlanId { get; }

    /// <summary>
    /// 获取关联的任务ID。
    /// </summary>
    public string TaskId { get; }

    /// <summary>
    /// 获取节点ID（主节点或从节点）。
    /// </summary>
    public string NodeId { get; }

    /// <summary>
    /// 获取同步对象的元数据。
    /// </summary>
    public SyncItemMetadata Metadata { get; }

    /// <summary>
    /// 获取文件状态。
    /// </summary>
    public FileHistoryState State { get; }

    /// <summary>
    /// 获取同步时间戳。
    /// </summary>
    public DateTimeOffset SyncTimestamp { get; }

    /// <summary>
    /// 获取同步版本号（用于追踪同步顺序）。
    /// </summary>
    public long SyncVersion { get; }

    /// <summary>
    /// 初始化 <see cref="SyncHistoryEntry"/> 类的新实例。
    /// </summary>
    public SyncHistoryEntry(
        string id,
        string planId,
        string taskId,
        string nodeId,
        SyncItemMetadata metadata,
        FileHistoryState state,
        DateTimeOffset syncTimestamp,
        long syncVersion)
    {
        Id = id;
        PlanId = planId;
        TaskId = taskId;
        NodeId = nodeId;
        Metadata = metadata;
        State = state;
        SyncTimestamp = syncTimestamp;
        SyncVersion = syncVersion;
    }
}

using UniversalSyncService.Abstractions.SyncItems;

namespace UniversalSyncService.Abstractions.SyncManagement.Engine;

/// <summary>
/// 表示冲突解决策略。
/// </summary>
public enum ConflictResolutionStrategy
{
    /// <summary>
    /// 总是保留本地版本。
    /// </summary>
    KeepLocal,

    /// <summary>
    /// 总是保留远程版本。
    /// </summary>
    KeepRemote,

    /// <summary>
    /// 保留较新的版本（基于修改时间）。
    /// </summary>
    KeepNewer,

    /// <summary>
    /// 保留较大的文件。
    /// </summary>
    KeepLarger,

    /// <summary>
    /// 重命名并保留两个版本。
    /// </summary>
    RenameBoth,

    /// <summary>
    /// 需要用户手动解决。
    /// </summary>
    Manual,
}

/// <summary>
/// 表示冲突信息。
/// </summary>
public interface ISyncConflict
{
    /// <summary>
    /// 获取冲突的文件路径。
    /// </summary>
    string FilePath { get; }

    /// <summary>
    /// 获取主节点当前文件状态。
    /// </summary>
    IFileStateSnapshot? MasterState { get; }

    /// <summary>
    /// 获取从节点当前文件状态。
    /// </summary>
    IFileStateSnapshot? SlaveState { get; }

    /// <summary>
    /// 获取主节点历史锚点状态（如果有）。
    /// </summary>
    IFileStateSnapshot? MasterHistoryState { get; }

    /// <summary>
    /// 获取从节点历史锚点状态（如果有）。
    /// </summary>
    IFileStateSnapshot? SlaveHistoryState { get; }

    /// <summary>
    /// 获取冲突检测时间。
    /// </summary>
    DateTimeOffset DetectedAt { get; }

    /// <summary>
    /// 获取冲突描述。
    /// </summary>
    string Description { get; }
}

/// <summary>
/// 表示冲突解决器。
/// </summary>
public interface IConflictResolver
{
    /// <summary>
    /// 获取或设置默认的冲突解决策略。
    /// </summary>
    ConflictResolutionStrategy DefaultStrategy { get; set; }

    /// <summary>
    /// 解决冲突。
    /// </summary>
    /// <param name="conflict">冲突信息。</param>
    /// <param name="strategy">解决策略，如果为null则使用默认策略。</param>
    /// <returns>解决后的同步决策。</returns>
    Task<SyncDecision> ResolveAsync(ISyncConflict conflict, ConflictResolutionStrategy? strategy = null);

    /// <summary>
    /// 批量解决冲突。
    /// </summary>
    /// <param name="conflicts">冲突列表。</param>
    /// <param name="strategy">解决策略。</param>
    /// <returns>文件路径到决策的映射。</returns>
    Task<Dictionary<string, SyncDecision>> ResolveBatchAsync(
        IEnumerable<ISyncConflict> conflicts,
        ConflictResolutionStrategy strategy);

    /// <summary>
    /// 当检测到冲突时触发。
    /// </summary>
    event Action<ISyncConflict>? OnConflictDetected;

    /// <summary>
    /// 当冲突被解决时触发。
    /// </summary>
    event Action<ISyncConflict, SyncDecision>? OnConflictResolved;
}

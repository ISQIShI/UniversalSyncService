using UniversalSyncService.Abstractions.SyncItems;
using UniversalSyncService.Abstractions.SyncManagement.History;

namespace UniversalSyncService.Abstractions.SyncManagement.Engine;

/// <summary>
/// 表示单个同步路径在一次决策计算时所需的完整上下文。
/// 这里显式区分主/从当前状态与各自历史锚点，避免执行层再自行覆写算法结果。
/// </summary>
public sealed class SyncPathSyncContext
{
    public SyncPathSyncContext(
        string path,
        SyncItemMetadata? masterMetadata,
        SyncItemMetadata? slaveMetadata,
        SyncHistoryEntry? masterHistoryEntry,
        SyncHistoryEntry? slaveHistoryEntry,
        bool isExplicitDeleteCandidate = false)
    {
        Path = path;
        MasterMetadata = masterMetadata;
        SlaveMetadata = slaveMetadata;
        MasterHistoryEntry = masterHistoryEntry;
        SlaveHistoryEntry = slaveHistoryEntry;
        IsExplicitDeleteCandidate = isExplicitDeleteCandidate;
    }

    /// <summary>
    /// 获取正在决策的相对路径。
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// 获取主节点当前扫描到的元数据；为空表示当前不存在。
    /// </summary>
    public SyncItemMetadata? MasterMetadata { get; }

    /// <summary>
    /// 获取从节点当前扫描到的元数据；为空表示当前不存在。
    /// </summary>
    public SyncItemMetadata? SlaveMetadata { get; }

    /// <summary>
    /// 获取主节点在上一个成功版本中的历史锚点。
    /// </summary>
    public SyncHistoryEntry? MasterHistoryEntry { get; }

    /// <summary>
    /// 获取从节点在上一个成功版本中的历史锚点。
    /// </summary>
    public SyncHistoryEntry? SlaveHistoryEntry { get; }

    /// <summary>
    /// 获取该路径是否为“显式删除候选”。
    /// 仅当该标记为 true 时，允许在双方当前扫描都不存在的情况下仍参与本轮决策。
    /// </summary>
    public bool IsExplicitDeleteCandidate { get; }
}

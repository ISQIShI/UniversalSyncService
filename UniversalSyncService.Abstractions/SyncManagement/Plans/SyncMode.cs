namespace UniversalSyncService.Abstractions.SyncManagement.Plans;

/// <summary>
/// 表示同步模式。
/// </summary>
public enum SyncMode
{
    /// <summary>
    /// 双向同步：主节点和从节点之间互相同步，保持两边一致。
    /// 基于算法：https://raw.githubusercontent.com/remotely-save/remotely-save/refs/heads/master/docs/sync_algorithm/v3/design.md
    /// </summary>
    Bidirectional,

    /// <summary>
    /// 拉取：从从节点拉取数据到主节点（主节点 ← 从节点）。
    /// 相当于增量拉取（Incremental Pull）。
    /// </summary>
    Pull,

    /// <summary>
    /// 推送：将主节点数据推送到从节点（主节点 → 从节点）。
    /// 相当于增量推送（Incremental Push）。
    /// </summary>
    Push,

    /// <summary>
    /// 拉取并删除：从从节点拉取数据到主节点，同时删除从节点上已拉取的数据。
    /// </summary>
    PullAndDelete,

    /// <summary>
    /// 推送并删除：将主节点数据推送到从节点，同时删除主节点上已推送的数据。
    /// </summary>
    PushAndDelete,
}

namespace UniversalSyncService.Abstractions.Nodes;

/// <summary>
/// 表示节点连接状态。
/// </summary>
public enum NodeState
{
    /// <summary>
    /// 未连接。
    /// </summary>
    Disconnected,

    /// <summary>
    /// 连接中。
    /// </summary>
    Connecting,

    /// <summary>
    /// 已连接。
    /// </summary>
    Connected,

    /// <summary>
    /// 发生错误。
    /// </summary>
    Error,
}

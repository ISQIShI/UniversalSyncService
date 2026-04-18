namespace UniversalSyncService.Abstractions.SyncManagement.Engine;

/// <summary>
/// 表示同步算法引擎。
/// 基于remotely-save的同步算法v3实现。
/// </summary>
public interface ISyncAlgorithmEngine
{
    /// <summary>
    /// 计算同步决策。
    /// 根据本地、远程和历史状态决定应该执行的操作。
    /// </summary>
    /// <param name="context">单个路径的完整同步上下文。</param>
    /// <param name="syncMode">同步模式。</param>
    /// <returns>同步决策。</returns>
    SyncDecision CalculateDecision(SyncPathSyncContext context, Plans.SyncMode syncMode);

    /// <summary>
    /// 批量计算同步决策。
    /// </summary>
    /// <param name="contexts">路径级同步上下文列表。</param>
    /// <param name="syncMode">同步模式。</param>
    /// <returns>文件路径到决策的映射。</returns>
    Task<Dictionary<string, SyncDecision>> CalculateDecisionsAsync(
        IEnumerable<SyncPathSyncContext> contexts,
        Plans.SyncMode syncMode);
}

using UniversalSyncService.Abstractions.SyncItems;
using UniversalSyncService.Abstractions.SyncManagement.Tasks;

namespace UniversalSyncService.Abstractions.SyncManagement.History;

/// <summary>
/// 表示同步历史记录管理器。
/// 存储和管理每次同步的文件状态，支持同步算法的实现。
/// </summary>
public interface ISyncHistoryManager
{
    /// <summary>
    /// 获取指定计划的最新历史版本号。
    /// </summary>
    /// <param name="planId">计划ID。</param>
    /// <returns>最新版本号，如果没有历史记录则返回0。</returns>
    Task<long> GetLatestVersionAsync(string planId);

    /// <summary>
    /// 获取指定计划的上一次成功同步的历史条目。
    /// </summary>
    /// <param name="planId">计划ID。</param>
    /// <param name="nodeId">节点ID（主节点或从节点）。</param>
    /// <returns>历史条目列表。</returns>
    Task<IReadOnlyList<SyncHistoryEntry>> GetPreviousSyncHistoryAsync(string planId, string nodeId);

    /// <summary>
    /// 获取指定文件在指定节点上的上一次历史记录。
    /// </summary>
    /// <param name="planId">计划ID。</param>
    /// <param name="nodeId">节点ID。</param>
    /// <param name="filePath">文件路径。</param>
    /// <returns>历史条目，如果不存在则返回null。</returns>
    Task<SyncHistoryEntry?> GetFileHistoryAsync(string planId, string nodeId, string filePath);

    /// <summary>
    /// 保存同步历史记录。
    /// 在同步成功后调用，记录本次同步的文件状态。
    /// </summary>
    /// <param name="entries">历史条目列表。</param>
    Task SaveHistoryAsync(IEnumerable<SyncHistoryEntry> entries);

    /// <summary>
    /// 清理旧的历史记录。
    /// </summary>
    /// <param name="planId">计划ID。</param>
    /// <param name="keepVersions">保留的历史版本数。</param>
    Task CleanupOldHistoryAsync(string planId, int keepVersions);

    /// <summary>
    /// 删除指定计划的所有历史记录。
    /// </summary>
    /// <param name="planId">计划ID。</param>
    Task DeletePlanHistoryAsync(string planId);

    /// <summary>
    /// 获取最近的同步历史记录。
    /// </summary>
    /// <param name="planId">可选计划ID；为空时返回所有计划的最近历史。</param>
    /// <param name="limit">返回记录上限。</param>
    Task<IReadOnlyList<SyncHistoryEntry>> GetRecentHistoryAsync(string? planId, int limit);

}

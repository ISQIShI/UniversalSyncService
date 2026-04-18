using UniversalSyncService.Abstractions.SyncManagement.Plans;

namespace UniversalSyncService.Abstractions.SyncManagement.Tasks;

/// <summary>
/// 表示同步任务生成器。
/// 将同步计划解析为多个具体的同步任务。
/// </summary>
public interface ISyncTaskGenerator
{
    /// <summary>
    /// 从同步计划生成任务列表。
    /// 每个从节点对应一个任务。
    /// </summary>
    /// <param name="plan">同步计划。</param>
    /// <returns>生成的任务列表。</returns>
    Task<IReadOnlyList<ISyncTask>> GenerateTasksAsync(SyncPlan plan);

    /// <summary>
    /// 验证计划是否可以生成有效的任务。
    /// </summary>
    /// <param name="plan">同步计划。</param>
    /// <returns>验证结果和错误信息。</returns>
    Task<(bool IsValid, IReadOnlyList<string> Errors)> ValidatePlanAsync(SyncPlan plan);
}

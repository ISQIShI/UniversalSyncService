using UniversalSyncService.Abstractions.SyncManagement.Plans;
using UniversalSyncService.Abstractions.SyncManagement.Tasks;

namespace UniversalSyncService.Abstractions.SyncManagement;

/// <summary>
/// 表示同步计划管理器。
/// 管理所有同步计划的生命周期，包括创建、更新、删除和执行调度。
/// </summary>
public interface ISyncPlanManager
{
    /// <summary>
    /// 获取所有同步计划。
    /// </summary>
    IReadOnlyList<SyncPlan> GetAllPlans();

    /// <summary>
    /// 根据ID获取同步计划。
    /// </summary>
    /// <param name="planId">计划ID。</param>
    /// <returns>同步计划，如果不存在则返回null。</returns>
    SyncPlan? GetPlanById(string planId);

    /// <summary>
    /// 创建新的同步计划。
    /// </summary>
    /// <param name="name">计划名称。</param>
    /// <param name="description">计划描述。</param>
    /// <param name="masterNodeId">主节点ID。</param>
    /// <param name="syncItemType">同步对象类型。</param>
    /// <param name="slaveConfigurations">从节点配置列表。</param>
    /// <param name="schedule">调度配置。</param>
    /// <returns>创建的同步计划。</returns>
    Task<SyncPlan> CreatePlanAsync(
        string name,
        string? description,
        string masterNodeId,
        string syncItemType,
        IEnumerable<SyncPlanSlaveConfiguration> slaveConfigurations,
        SyncSchedule schedule);

    /// <summary>
    /// 更新同步计划。
    /// </summary>
    /// <param name="planId">计划ID。</param>
    /// <param name="updates">更新操作。</param>
    /// <returns>更新后的计划。</returns>
    Task<SyncPlan> UpdatePlanAsync(string planId, Action<SyncPlan> updates);

    /// <summary>
    /// 使用完整配置替换一个已有同步计划。
    /// 适用于控制台编辑场景，允许同时更新主节点、同步对象类型、从节点列表和调度配置。
    /// </summary>
    /// <param name="planId">计划 ID。</param>
    /// <param name="name">计划名称。</param>
    /// <param name="description">计划描述。</param>
    /// <param name="masterNodeId">主节点 ID。</param>
    /// <param name="syncItemType">同步对象类型。</param>
    /// <param name="slaveConfigurations">从节点配置。</param>
    /// <param name="schedule">调度配置。</param>
    /// <param name="isEnabled">是否启用。</param>
    /// <returns>替换后的计划。</returns>
    Task<SyncPlan> ReplacePlanAsync(
        string planId,
        string name,
        string? description,
        string masterNodeId,
        string syncItemType,
        IEnumerable<SyncPlanSlaveConfiguration> slaveConfigurations,
        SyncSchedule schedule,
        bool isEnabled);

    /// <summary>
    /// 删除同步计划。
    /// </summary>
    /// <param name="planId">计划ID。</param>
    Task DeletePlanAsync(string planId);

    /// <summary>
    /// 启用同步计划。
    /// </summary>
    /// <param name="planId">计划ID。</param>
    Task EnablePlanAsync(string planId);

    /// <summary>
    /// 禁用同步计划。
    /// </summary>
    /// <param name="planId">计划ID。</param>
    Task DisablePlanAsync(string planId);

    /// <summary>
    /// 立即执行同步计划（手动触发）。
    /// </summary>
    /// <param name="planId">计划ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>执行结果。</returns>
    Task<Dictionary<string, SyncTaskResult>> ExecutePlanNowAsync(string planId, CancellationToken cancellationToken);

    /// <summary>
    /// 验证同步计划配置是否有效。
    /// </summary>
    /// <param name="planId">计划ID。</param>
    /// <returns>验证结果和错误信息。</returns>
    Task<(bool IsValid, IReadOnlyList<string> Errors)> ValidatePlanAsync(string planId);

    /// <summary>
    /// 当计划被创建时触发。
    /// </summary>
    event Action<SyncPlan>? OnPlanCreated;

    /// <summary>
    /// 当计划被更新时触发。
    /// </summary>
    event Action<SyncPlan>? OnPlanUpdated;

    /// <summary>
    /// 当计划被删除时触发。
    /// </summary>
    event Action<SyncPlan>? OnPlanDeleted;

    /// <summary>
    /// 当计划状态改变时触发（启用/禁用）。
    /// </summary>
    event Action<SyncPlan, bool>? OnPlanStatusChanged;
}

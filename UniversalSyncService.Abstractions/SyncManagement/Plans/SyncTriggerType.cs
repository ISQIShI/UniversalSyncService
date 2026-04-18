namespace UniversalSyncService.Abstractions.SyncManagement.Plans;

/// <summary>
/// 表示同步计划触发类型。
/// </summary>
public enum SyncTriggerType
{
    /// <summary>
    /// 手动触发。
    /// </summary>
    Manual,

    /// <summary>
    /// 定时触发。
    /// </summary>
    Scheduled,

    /// <summary>
    /// 实时触发。
    /// </summary>
    Realtime,
}

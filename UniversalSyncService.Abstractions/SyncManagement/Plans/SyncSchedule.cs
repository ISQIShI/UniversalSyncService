namespace UniversalSyncService.Abstractions.SyncManagement.Plans;

/// <summary>
/// 表示同步调度配置。
/// </summary>
public sealed class SyncSchedule
{
    /// <summary>
    /// 获取触发方式类型。
    /// </summary>
    public SyncTriggerType TriggerType { get; }

    /// <summary>
    /// 获取或设置Cron表达式（用于定时触发）。
    /// 例如："0 0 * * *" 表示每小时执行一次。
    /// </summary>
    public string? CronExpression { get; set; }

    /// <summary>
    /// 获取或设置时间间隔（用于定时触发，作为Cron的替代）。
    /// </summary>
    public TimeSpan? Interval { get; set; }

    /// <summary>
    /// 获取或设置计划的下一次执行时间。
    /// </summary>
    public DateTimeOffset? NextScheduledTime { get; set; }

    /// <summary>
    /// 获取或设置是否启用实时监听（用于实时同步）。
    /// </summary>
    public bool EnableFileSystemWatcher { get; set; }

    /// <summary>
    /// 初始化 <see cref="SyncSchedule"/> 类的新实例。
    /// </summary>
    public SyncSchedule(SyncTriggerType triggerType)
    {
        TriggerType = triggerType;
        EnableFileSystemWatcher = false;
    }

    /// <summary>
    /// 计算下一次执行时间。
    /// </summary>
    /// <returns>下一次执行时间，如果不适用则返回null。</returns>
    public DateTimeOffset? CalculateNextRunTime(DateTimeOffset? fromTime = null)
    {
        var referenceTime = fromTime ?? DateTimeOffset.Now;

        return TriggerType switch
        {
            SyncTriggerType.Manual => null,
            SyncTriggerType.Realtime => referenceTime.Add(Interval ?? TimeSpan.FromMinutes(1)),
            SyncTriggerType.Scheduled when Interval.HasValue => referenceTime.Add(Interval.Value),
            SyncTriggerType.Scheduled when !string.IsNullOrWhiteSpace(CronExpression) => referenceTime.AddHours(1),
            _ => NextScheduledTime ?? referenceTime.AddHours(1)
        };
    }
}

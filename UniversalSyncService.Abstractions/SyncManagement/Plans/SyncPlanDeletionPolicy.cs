namespace UniversalSyncService.Abstractions.SyncManagement.Plans;

/// <summary>
/// 同步计划删除阈值触发后的 fail-safe 行为。
/// </summary>
public enum SyncPlanFailSafeMode
{
    /// <summary>
    /// 直接阻断当前同步任务，不执行任何删除动作。
    /// </summary>
    Block,

    /// <summary>
    /// 暂停并等待管理员确认（通过显式 override）后再执行。
    /// </summary>
    Confirm,

    /// <summary>
    /// 仅记录高等级告警并继续执行（仅建议用于开发/测试环境）。
    /// </summary>
    Ignore
}

/// <summary>
/// 同步计划级删除守卫策略。
/// </summary>
public sealed class SyncPlanDeletionPolicy
{
    public const int DefaultDeleteThreshold = 100;
    public const double DefaultPercentThreshold = 10d;

    /// <summary>
    /// 获取或设置每轮同步允许的最大删除候选数量。
    /// 默认值为 100。
    /// </summary>
    public int DeleteThreshold { get; set; } = DefaultDeleteThreshold;

    /// <summary>
    /// 获取或设置每轮同步允许删除候选占总上下文条目的百分比（0-100）。
    /// 默认值为 10%。
    /// </summary>
    public double PercentThreshold { get; set; } = DefaultPercentThreshold;

    /// <summary>
    /// 获取或设置阈值触发后的 fail-safe 模式。
    /// 默认值为 Block（生产环境保守策略）。
    /// </summary>
    public SyncPlanFailSafeMode FailSafeMode { get; set; } = SyncPlanFailSafeMode.Block;

    /// <summary>
    /// 获取或设置是否允许本轮显式越过阈值保护。
    /// 该字段用于管理员一次性确认（confirm 模式）。
    /// </summary>
    public bool AllowThresholdBreachForCurrentRun { get; set; }

    /// <summary>
    /// 获取或设置管理员阈值越权原因。
    /// </summary>
    public string? ThresholdOverrideReason { get; set; }

    /// <summary>
    /// 克隆当前策略对象，避免外部引用共享导致的隐式修改。
    /// </summary>
    public SyncPlanDeletionPolicy Clone()
    {
        return new SyncPlanDeletionPolicy
        {
            DeleteThreshold = DeleteThreshold,
            PercentThreshold = PercentThreshold,
            FailSafeMode = FailSafeMode,
            AllowThresholdBreachForCurrentRun = AllowThresholdBreachForCurrentRun,
            ThresholdOverrideReason = ThresholdOverrideReason
        };
    }
}

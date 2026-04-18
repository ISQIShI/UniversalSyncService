namespace UniversalSyncService.Abstractions.SyncManagement.Plans;

/// <summary>
/// 表示同步计划。
/// 用户可以在前台UI预设多个同步计划，每个计划定义了主节点与哪些从节点同步、如何同步、何时同步。
/// </summary>
public sealed class SyncPlan
{
    /// <summary>
    /// 获取计划的唯一标识符。
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// 获取或设置计划的显示名称。
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// 获取或设置计划的描述。
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// 获取主节点ID（运行同步服务的本地节点）。
    /// </summary>
    public string MasterNodeId { get; }

    /// <summary>
    /// 获取同步对象类型（模式）。
    /// 例如：文件系统、Eagle库、Notion数据库等。
    /// </summary>
    public string SyncItemType { get; }

    /// <summary>
    /// 获取从节点配置列表。
    /// </summary>
    public List<SyncPlanSlaveConfiguration> SlaveConfigurations { get; }

    /// <summary>
    /// 获取同步调度配置。
    /// </summary>
    public SyncSchedule Schedule { get; }

    /// <summary>
    /// 获取或设置计划是否已启用。
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// 获取计划的创建时间。
    /// </summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// 获取或设置计划的最后修改时间。
    /// </summary>
    public DateTimeOffset ModifiedAt { get; set; }

    /// <summary>
    /// 获取或设置计划的最后执行时间。
    /// </summary>
    public DateTimeOffset? LastExecutedAt { get; set; }

    /// <summary>
    /// 获取或设置计划的执行次数。
    /// </summary>
    public int ExecutionCount { get; set; }

    /// <summary>
    /// 初始化 <see cref="SyncPlan"/> 类的新实例。
    /// </summary>
    public SyncPlan(
        string id,
        string name,
        string masterNodeId,
        string syncItemType,
        List<SyncPlanSlaveConfiguration> slaveConfigurations,
        SyncSchedule schedule,
        DateTimeOffset? createdAt = null)
    {
        var actualCreatedAt = createdAt ?? DateTimeOffset.Now;

        Id = id;
        Name = name;
        MasterNodeId = masterNodeId;
        SyncItemType = syncItemType;
        SlaveConfigurations = slaveConfigurations;
        Schedule = schedule;
        CreatedAt = actualCreatedAt;
        ModifiedAt = actualCreatedAt;
        IsEnabled = true;
        ExecutionCount = 0;
    }

    /// <summary>
    /// 更新计划的修改时间。
    /// </summary>
    public void Touch()
    {
        ModifiedAt = DateTimeOffset.Now;
    }
}

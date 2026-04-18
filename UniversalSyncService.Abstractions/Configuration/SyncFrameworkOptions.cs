using UniversalSyncService.Abstractions.SyncManagement.Plans;
using UniversalSyncService.Abstractions.SyncManagement.Engine;

namespace UniversalSyncService.Abstractions.Configuration;

public sealed class SyncFrameworkOptions
{
    public const string DefaultHostNodeId = "host-local";

    public const string SectionName = "UniversalSyncService:Sync";

    [ConfigComment("是否启用同步框架。")]
    public bool EnableSyncFramework { get; set; } = true;

    [ConfigComment("调度器轮询间隔（秒）。")]
    public int SchedulerPollingIntervalSeconds { get; set; } = 30;

    [ConfigComment("任务执行最大并发数。")]
    public int MaxConcurrentTasks { get; set; } = 1;

    [ConfigComment("同步历史保留版本数。")]
    public int HistoryRetentionVersions { get; set; } = 20;

    [ConfigComment("同步历史存储文件路径（相对 ContentRoot 或绝对路径）。")]
    public string HistoryStorePath { get; set; } = "data/sync-history.db";

    [ConfigComment("隐式本地主节点在内容根目录下映射的工作目录。")] 
    public string HostWorkspacePath { get; set; } = "sync-test/master";

    [ConfigComment("已配置的节点列表。")]
    public List<ConfiguredNodeOptions> Nodes { get; set; } = [];

    [ConfigComment("同步计划列表。")]
    public List<SyncPlanOptions> Plans { get; set; } = [];
}

public sealed class ConfiguredNodeOptions
{
    [ConfigComment("节点唯一标识。")]
    public string Id { get; set; } = string.Empty;

    [ConfigComment("节点显示名称。")]
    public string Name { get; set; } = string.Empty;

    [ConfigComment("节点类型标识，例如 Local、WebDAV、OneDrive。")]
    public string NodeType { get; set; } = string.Empty;

    [ConfigComment("节点连接配置。")]
    public Dictionary<string, string> ConnectionSettings { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [ConfigComment("节点自定义选项。")]
    public Dictionary<string, string> CustomOptions { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [ConfigComment("节点创建时间。")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    [ConfigComment("节点最后修改时间。")]
    public DateTimeOffset ModifiedAt { get; set; } = DateTimeOffset.Now;

    [ConfigComment("节点是否启用。")]
    public bool IsEnabled { get; set; } = true;
}

public sealed class SyncPlanOptions
{
    [ConfigComment("计划唯一标识。")]
    public string Id { get; set; } = string.Empty;

    [ConfigComment("计划显示名称。")]
    public string Name { get; set; } = string.Empty;

    [ConfigComment("计划描述。")]
    public string? Description { get; set; }

    [ConfigComment("主节点 ID。")]
    public string MasterNodeId { get; set; } = string.Empty;

    [ConfigComment("同步对象类型标识。")]
    public string SyncItemType { get; set; } = string.Empty;

    [ConfigComment("从节点配置列表。")]
    public List<SyncPlanSlaveConfigurationOptions> SlaveConfigurations { get; set; } = [];

    [ConfigComment("调度配置。")]
    public SyncScheduleOptions Schedule { get; set; } = new();

    [ConfigComment("计划是否启用。")]
    public bool IsEnabled { get; set; } = true;

    [ConfigComment("计划创建时间。")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    [ConfigComment("计划最后修改时间。")]
    public DateTimeOffset ModifiedAt { get; set; } = DateTimeOffset.Now;

    [ConfigComment("计划最后执行时间。")]
    public DateTimeOffset? LastExecutedAt { get; set; }

    [ConfigComment("计划累计执行次数。")]
    public int ExecutionCount { get; set; }
}

public sealed class SyncPlanSlaveConfigurationOptions
{
    [ConfigComment("从节点 ID。")]
    public string SlaveNodeId { get; set; } = string.Empty;

    [ConfigComment("同步模式。")]
    public SyncMode SyncMode { get; set; } = SyncMode.Bidirectional;

    [ConfigComment("源路径。")]
    public string? SourcePath { get; set; }

    [ConfigComment("目标路径。")]
    public string? TargetPath { get; set; }

    [ConfigComment("是否启用删除保护。")] 
    public bool EnableDeletionProtection { get; set; } = true;

    [ConfigComment("冲突处理策略。默认 Manual，可选 KeepNewer 等。")]
    public ConflictResolutionStrategy ConflictResolutionStrategy { get; set; } = ConflictResolutionStrategy.Manual;

    [ConfigComment("包含过滤规则。")]
    public List<string> Filters { get; set; } = [];

    [ConfigComment("排除规则。")]
    public List<string> Exclusions { get; set; } = [];
}

public sealed class SyncScheduleOptions
{
    [ConfigComment("触发方式。")]
    public SyncTriggerType TriggerType { get; set; } = SyncTriggerType.Manual;

    [ConfigComment("Cron 表达式。当前仅占位保存，不参与解析。")]
    public string? CronExpression { get; set; }

    [ConfigComment("时间间隔。")]
    public TimeSpan? Interval { get; set; }

    [ConfigComment("下一次计划执行时间。")]
    public DateTimeOffset? NextScheduledTime { get; set; }

    [ConfigComment("是否启用文件系统监听。当前仅作为配置保留。")]
    public bool EnableFileSystemWatcher { get; set; }
}

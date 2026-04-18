using UniversalSyncService.Abstractions.SyncManagement.Engine;

namespace UniversalSyncService.Abstractions.SyncManagement.Plans;

/// <summary>
/// 表示同步计划中的从节点配置。
/// </summary>
public sealed class SyncPlanSlaveConfiguration
{
    /// <summary>
    /// 获取从节点的ID。
    /// </summary>
    public string SlaveNodeId { get; }

    /// <summary>
    /// 获取或设置与此从节点的同步模式。
    /// </summary>
    public SyncMode SyncMode { get; set; }

    /// <summary>
    /// 获取或设置同步的源路径（在从节点上）。
    /// 如果为null，则同步整个节点。
    /// </summary>
    public string? SourcePath { get; set; }

    /// <summary>
    /// 获取或设置同步的目标路径（在主节点上）。
    /// 如果为null，则使用默认路径。
    /// </summary>
    public string? TargetPath { get; set; }

    /// <summary>
    /// 获取或设置是否启用删除保护。
    /// 如果启用，在检测到删除操作时需要用户确认。
    /// </summary>
    public bool EnableDeletionProtection { get; set; }

    /// <summary>
    /// 获取或设置冲突处理策略。
    /// 当前默认保留人工处理，将来可由用户在控制面中选择。
    /// </summary>
    public ConflictResolutionStrategy ConflictResolutionStrategy { get; set; }

    /// <summary>
    /// 获取或设置文件过滤器（支持通配符）。
    /// 例如：["*.jpg", "*.png"] 只同步图片文件。
    /// </summary>
    public List<string>? Filters { get; set; }

    /// <summary>
    /// 获取或设置排除规则（支持通配符）。
    /// 例如：["*.tmp", ".git/"] 排除临时文件和git目录。
    /// </summary>
    public List<string>? Exclusions { get; set; }

    /// <summary>
    /// 初始化 <see cref="SyncPlanSlaveConfiguration"/> 类的新实例。
    /// </summary>
    public SyncPlanSlaveConfiguration(
        string slaveNodeId,
        SyncMode syncMode)
    {
        SlaveNodeId = slaveNodeId;
        SyncMode = syncMode;
        EnableDeletionProtection = true;
        ConflictResolutionStrategy = ConflictResolutionStrategy.Manual;
        Filters = new List<string>();
        Exclusions = new List<string>();
    }
}

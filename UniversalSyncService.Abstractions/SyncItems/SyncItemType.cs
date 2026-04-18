namespace UniversalSyncService.Abstractions.SyncItems;

/// <summary>
/// 表示同步项类型。
/// </summary>
public enum SyncItemType
{
    /// <summary>
    /// 文件。
    /// </summary>
    File,

    /// <summary>
    /// 目录。
    /// </summary>
    Directory,

    /// <summary>
    /// 容器（例如自定义格式库）。
    /// </summary>
    Container
}

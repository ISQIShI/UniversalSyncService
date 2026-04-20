namespace UniversalSyncService.Abstractions.SyncManagement.Engine;

/// <summary>
/// 表示同步决策。
/// 根据remotely-save的同步算法v3，定义在特定情况下应该执行的操作。
/// </summary>
public enum SyncDecision
{
    /// <summary>
    /// 不执行任何操作。
    /// </summary>
    DoNothing,

    /// <summary>
    /// 将本地修改推送到远程。
    /// </summary>
    Push,

    /// <summary>
    /// 从远程拉取修改到本地。
    /// </summary>
    Pull,

    /// <summary>
    /// 删除本地文件。
    /// </summary>
    DeleteLocal,

    /// <summary>
    /// 删除远程文件。
    /// </summary>
    DeleteRemote,

    /// <summary>
    /// 清理历史记录（文件在两边都被删除）。
    /// </summary>
    CleanHistory,

    /// <summary>
    /// 发生冲突，需要用户解决。
    /// </summary>
    Conflict,

    /// <summary>
    /// 重命名冲突文件。
    /// </summary>
    ConflictRename,
}

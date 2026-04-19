namespace UniversalSyncService.Abstractions.SyncItems;

/// <summary>
/// 定义计划层与运行时共享的同步对象类型标识。
/// </summary>
public static class SyncItemKinds
{
    /// <summary>
    /// 文件系统同步对象类型。
    /// </summary>
    public const string FileSystem = "FileSystem";

    /// <summary>
    /// 判断给定值是否为文件系统同步对象类型。
    /// </summary>
    public static bool IsFileSystem(string? value)
    {
        return string.Equals(value, FileSystem, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 归一化同步对象类型。
    /// </summary>
    public static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("同步对象类型不能为空。", nameof(value));
        }

        return IsFileSystem(value)
            ? FileSystem
            : value.Trim();
    }
}

using Microsoft.Data.Sqlite;

namespace UniversalSyncService.Testing;

/// <summary>
/// 管理测试临时内容根目录的创建与清理。
/// </summary>
public sealed class TempContentRoot : IAsyncDisposable
{
    private readonly string _rootPath;
    private readonly bool _copyBuiltWebAssets;

    public string RootPath => _rootPath;

    private TempContentRoot(string rootPath, bool copyBuiltWebAssets)
    {
        _rootPath = rootPath;
        _copyBuiltWebAssets = copyBuiltWebAssets;
    }

    public static Task<TempContentRoot> CreateAsync(string scopeName, bool copyBuiltWebAssets = false)
    {
        var rootPath = Path.Combine(Path.GetTempPath(), scopeName, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootPath);

        var instance = new TempContentRoot(rootPath, copyBuiltWebAssets);
        if (copyBuiltWebAssets)
        {
            instance.CopyBuiltWebAssets();
        }

        return Task.FromResult(instance);
    }

    public string Resolve(params string[] segments)
    {
        if (segments.Length == 0)
        {
            return _rootPath;
        }

        return Path.Combine([_rootPath, .. segments]);
    }

    public async ValueTask DisposeAsync()
    {
        if (!Directory.Exists(_rootPath))
        {
            return;
        }

        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                // 兜底清理当前进程内 SQLite 连接池，避免历史库句柄阻塞临时目录删除。
                SqliteConnection.ClearAllPools();
                Directory.Delete(_rootPath, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 4)
            {
                // 文件可能被占用，短暂延迟后重试
                await Task.Delay(200);
            }
            catch (UnauthorizedAccessException) when (attempt < 4)
            {
                // 权限问题，短暂延迟后重试
                await Task.Delay(200);
            }
            // 其他异常或最后一次重试失败时，允许抛出异常以暴露问题
        }
    }

    private void CopyBuiltWebAssets()
    {
        if (!_copyBuiltWebAssets)
        {
            return;
        }

        var sourceWwwroot = Path.Combine(Directory.GetCurrentDirectory(), "UniversalSyncService.Host", "wwwroot");
        if (!Directory.Exists(sourceWwwroot))
        {
            return;
        }

        var targetWwwroot = Path.Combine(_rootPath, "wwwroot");
        Directory.CreateDirectory(targetWwwroot);

        foreach (var directory in Directory.GetDirectories(sourceWwwroot, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(directory.Replace(sourceWwwroot, targetWwwroot));
        }

        foreach (var file in Directory.GetFiles(sourceWwwroot, "*", SearchOption.AllDirectories))
        {
            var targetFilePath = file.Replace(sourceWwwroot, targetWwwroot);
            var targetDirectoryPath = Path.GetDirectoryName(targetFilePath);
            if (!string.IsNullOrWhiteSpace(targetDirectoryPath))
            {
                Directory.CreateDirectory(targetDirectoryPath);
            }

            File.Copy(file, targetFilePath, overwrite: true);
        }
    }
}

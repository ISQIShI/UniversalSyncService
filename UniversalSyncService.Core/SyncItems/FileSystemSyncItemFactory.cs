using UniversalSyncService.Abstractions.SyncItems;

namespace UniversalSyncService.Core.SyncItems;

/// <summary>
/// 文件系统同步项工厂。
/// </summary>
public sealed class FileSystemSyncItemFactory : ISyncItemFactory
{
    public Task<ISyncItem> CreateFromPathAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(path);

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
        {
            throw new FileNotFoundException("指定路径不存在，无法创建同步对象。", fullPath);
        }

        ISyncItem item = new FileSystemSyncItem(fullPath, Path.GetFileName(fullPath));
        return Task.FromResult(item);
    }

    public async Task<ISyncItem> CreateFromStreamAsync(Stream stream, SyncItemMetadata metadata, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(metadata);

        // 使用临时文件承接外部流，方便后续统一按文件系统同步项处理。
        var tempDirectory = Path.Combine(Path.GetTempPath(), "UniversalSyncService", "SyncItems");
        Directory.CreateDirectory(tempDirectory);
        var tempFilePath = Path.Combine(tempDirectory, $"{Guid.NewGuid():N}-{metadata.Name}");

        await using (var fileStream = new FileStream(tempFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            await stream.CopyToAsync(fileStream, cancellationToken);
        }

        return new FileSystemSyncItem(tempFilePath, metadata.Path);
    }

    public bool SupportsPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Path.IsPathRooted(path) || path.Contains(Path.DirectorySeparatorChar) || path.Contains(Path.AltDirectorySeparatorChar);
    }
}

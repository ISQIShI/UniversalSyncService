using System.Runtime.CompilerServices;
using UniversalSyncService.Abstractions.Nodes;
using UniversalSyncService.Abstractions.SyncItems;
using UniversalSyncService.Core.Providers;
using UniversalSyncService.Core.SyncItems;

namespace UniversalSyncService.Core.Nodes;

/// <summary>
/// 本地节点实现。
/// 该节点把指定根目录视作一个可读写、可删除的同步端点。
/// 支持处理任意 ISyncItem，不仅限于 FileSystemSyncItem。
/// </summary>
public sealed class LocalNode : INode
{
    private readonly string _rootPath;
    private readonly HashSet<string> _excludedAbsolutePaths;
    private readonly SyncItemFactoryRegistry _syncItemFactoryRegistry;

    public LocalNode(
        string id,
        string name,
        string rootPath,
        IEnumerable<string>? excludedAbsolutePaths,
        SyncItemFactoryRegistry syncItemFactoryRegistry)
    {
        Metadata = new NodeMetadata(id, name, NodeType.Local, "1.0.0", "本地文件系统节点");
        Capabilities = NodeCapabilities.CanRead | NodeCapabilities.CanWrite | NodeCapabilities.CanDelete | NodeCapabilities.CanStream;
        State = NodeState.Disconnected;

        _rootPath = rootPath;
        _excludedAbsolutePaths = excludedAbsolutePaths is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(excludedAbsolutePaths.Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
        _syncItemFactoryRegistry = syncItemFactoryRegistry;
    }

    public NodeMetadata Metadata { get; }

    public NodeCapabilities Capabilities { get; }

    public NodeState State { get; private set; }

    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        State = NodeState.Connecting;

        // 本地节点以目录存在性为连接成功的判定依据，不依赖外部网络会话。
        Directory.CreateDirectory(_rootPath);
        State = NodeState.Connected;
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        State = NodeState.Disconnected;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 获取可同步项列表。
    /// 枚举本地文件系统，为每个文件/目录创建 FileSystemSyncItem。
    /// </summary>
    public async IAsyncEnumerable<ISyncItem> GetSyncItemsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Directory.Exists(_rootPath))
        {
            yield break;
        }

        // 统一按根目录下的相对路径输出元数据，保证算法层比较的是"同一逻辑文件"。
        await foreach (var item in EnumerateLocalSyncItemsAsync(_rootPath, cancellationToken))
        {
            yield return item;
        }
    }

    /// <summary>
    /// 上传同步项到本地节点。
    /// 支持任意 ISyncItem，不仅限于 FileSystemSyncItem。
    /// </summary>
    public async Task UploadAsync(ISyncItem item, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(item);

        var targetPath = ResolveAbsolutePath(item.Metadata.Path);

        if (item.ItemType == SyncItemType.Directory)
        {
            await CreateLocalDirectoryAsync(targetPath, item.Metadata, cancellationToken);
            return;
        }

        await WriteFileFromSyncItemAsync(item, targetPath, cancellationToken);
    }

    /// <summary>
    /// 从本地节点下载同步项。
    /// 对本地节点而言，"下载到节点"与"上传到节点"的落盘过程一致。
    /// </summary>
    public Task DownloadAsync(ISyncItem item, CancellationToken cancellationToken)
    {
        return UploadAsync(item, cancellationToken);
    }

    /// <summary>
    /// 删除本地节点上的相对路径项。
    /// </summary>
    public Task DeleteAsync(string relativePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(relativePath);

        var absolutePath = ResolveAbsolutePath(relativePath);

        if (File.Exists(absolutePath))
        {
            File.Delete(absolutePath);
        }
        else if (Directory.Exists(absolutePath))
        {
            Directory.Delete(absolutePath, recursive: true);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 枚举本地文件系统的同步项。
    /// </summary>
    private async IAsyncEnumerable<FileSystemSyncItem> EnumerateLocalSyncItemsAsync(
        string rootPath,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // 枚举所有目录
        foreach (var directory in Directory.EnumerateDirectories(rootPath, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (ShouldSkipPath(directory))
            {
                continue;
            }

            var relativePath = GetRelativePathFromRoot(directory);
            yield return new FileSystemSyncItem(directory, relativePath);
        }

        // 枚举所有文件
        foreach (var file in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (ShouldSkipPath(file))
            {
                continue;
            }

            var relativePath = GetRelativePathFromRoot(file);
            yield return new FileSystemSyncItem(file, relativePath);
        }

        await Task.CompletedTask; // 满足 async 方法要求
    }

    /// <summary>
    /// 创建本地目录并设置元数据。
    /// </summary>
    private static Task CreateLocalDirectoryAsync(
        string targetPath,
        SyncItemMetadata metadata,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(targetPath);

        if (metadata.ModifiedAt.HasValue)
        {
            Directory.SetLastWriteTimeUtc(targetPath, metadata.ModifiedAt.Value.UtcDateTime);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 从 ISyncItem 写入文件到本地路径。
    /// </summary>
    private static async Task WriteFileFromSyncItemAsync(
        ISyncItem item,
        string targetPath,
        CancellationToken cancellationToken)
    {
        // 确保目标目录存在
        var targetDirectory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(targetDirectory))
        {
            Directory.CreateDirectory(targetDirectory);
        }

        // 从源同步项读取流并写入本地文件
        await using (var sourceStream = await item.OpenReadAsync(cancellationToken))
        await using (var targetStream = new FileStream(
            targetPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            81920,
            useAsync: true))
        {
            await sourceStream.CopyToAsync(targetStream, cancellationToken);
            await targetStream.FlushAsync(cancellationToken);
        }

        // 先关闭写入流，再补齐源端时间戳，避免文件句柄释放时把最后写入时间覆盖成"关闭时刻"。
        ApplyMetadataTimestamps(targetPath, item.Metadata);
    }

    /// <summary>
    /// 应用元数据时间戳到本地文件。
    /// </summary>
    private static void ApplyMetadataTimestamps(string filePath, SyncItemMetadata metadata)
    {
        if (metadata.CreatedAt.HasValue)
        {
            File.SetCreationTimeUtc(filePath, metadata.CreatedAt.Value.UtcDateTime);
        }

        if (metadata.ModifiedAt.HasValue)
        {
            File.SetLastWriteTimeUtc(filePath, metadata.ModifiedAt.Value.UtcDateTime);
        }
    }

    /// <summary>
    /// 获取相对于根目录的路径。
    /// </summary>
    private string GetRelativePathFromRoot(string absolutePath)
    {
        return Path.GetRelativePath(_rootPath, absolutePath).Replace("\\", "/");
    }

    /// <summary>
    /// 将相对路径解析为绝对路径。
    /// </summary>
    private string ResolveAbsolutePath(string relativePath)
    {
        var normalizedPath = relativePath.Replace('/', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.Combine(_rootPath, normalizedPath));
    }

    /// <summary>
    /// 检查路径是否应该被跳过。
    /// </summary>
    private bool ShouldSkipPath(string absolutePath)
    {
        return _excludedAbsolutePaths.Contains(Path.GetFullPath(absolutePath));
    }
}

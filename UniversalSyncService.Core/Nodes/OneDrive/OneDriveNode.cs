using System.Net;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using UniversalSyncService.Abstractions.Nodes;
using UniversalSyncService.Abstractions.SyncItems;
using UniversalSyncService.Core.SyncItems;

namespace UniversalSyncService.Core.Nodes.OneDrive;

/// <summary>
/// OneDrive 节点实现。
/// 通过 Microsoft Graph API 与 OneDrive 进行文件同步。
/// </summary>
public sealed class OneDriveNode : INode
{
    private readonly OneDriveNodeOptions _options;
    private readonly GraphServiceClient _graphClient;
    private readonly ILogger<OneDriveNode> _logger;
    private string? _driveId;
    private string? _rootItemId;

    public OneDriveNode(
        string id,
        string name,
        OneDriveNodeOptions options,
        GraphServiceClient graphClient,
        ILogger<OneDriveNode> logger)
    {
        Metadata = new NodeMetadata(id, name, NodeType.Cloud, "1.0.0", "Microsoft OneDrive 云存储节点");
        Capabilities = NodeCapabilities.CanRead 
            | NodeCapabilities.CanWrite 
            | NodeCapabilities.CanDelete 
            | NodeCapabilities.CanStream;
        State = NodeState.Disconnected;

        _options = options ?? throw new ArgumentNullException(nameof(options));
        _graphClient = graphClient ?? throw new ArgumentNullException(nameof(graphClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public NodeMetadata Metadata { get; }
    public NodeCapabilities Capabilities { get; }
    public IReadOnlySet<string> SupportedSyncItemKinds { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { SyncItemKinds.FileSystem };
    public NodeState State { get; private set; }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        State = NodeState.Connecting;
        _logger.LogInformation("正在连接 OneDrive 节点...");

        try
        {
            // 获取驱动器信息
            Drive? drive = await ExecuteWithRetryAsync(
                async () =>
                {
                    if (_options.DriveSelector == "DriveId" && !string.IsNullOrEmpty(_options.DriveId))
                    {
                        return await _graphClient.Drives[_options.DriveId].GetAsync(cancellationToken: cancellationToken);
                    }
                    else
                    {
                        return await _graphClient.Me.Drive.GetAsync(cancellationToken: cancellationToken);
                    }
                },
                cancellationToken);

            _driveId = drive?.Id;

            if (string.IsNullOrEmpty(_driveId))
            {
                throw new InvalidOperationException("无法获取 OneDrive 驱动器 ID");
            }

            // 获取根目录项 ID
            var rootItem = await ExecuteWithRetryAsync(
                async () => await _graphClient.Drives[_driveId].Root.GetAsync(cancellationToken: cancellationToken),
                cancellationToken);
            _rootItemId = rootItem?.Id;

            if (string.IsNullOrEmpty(_rootItemId))
            {
                throw new InvalidOperationException("无法获取 OneDrive 根目录 ID");
            }

            // 如果配置了根路径，验证其存在性
            if (!string.IsNullOrEmpty(_options.RootPath))
            {
                var rootFolder = await GetOrCreateFolderAsync(_options.RootPath, cancellationToken);
                if (rootFolder?.Id != null)
                {
                    _rootItemId = rootFolder.Id;
                }
            }

            State = NodeState.Connected;
            _logger.LogInformation("OneDrive 节点连接成功，驱动器 ID: {DriveId}", _driveId);
        }
        catch (Exception ex)
        {
            State = NodeState.Disconnected;
            _logger.LogError(ex, "OneDrive 节点连接失败");
            throw;
        }
    }

    public Task DisconnectAsync(CancellationToken cancellationToken)
    {
        State = NodeState.Disconnected;
        _driveId = null;
        _rootItemId = null;
        _logger.LogInformation("OneDrive 节点已断开连接");
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<ISyncItem> GetSyncItemsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (State != NodeState.Connected)
        {
            throw new InvalidOperationException("节点未连接");
        }

        if (string.IsNullOrEmpty(_driveId) || string.IsNullOrEmpty(_rootItemId))
        {
            throw new InvalidOperationException("驱动器或根目录未初始化");
        }

        var queue = new Queue<(string itemId, string relativePath)>();
        queue.Enqueue((_rootItemId, string.Empty));

        while (queue.Count > 0 && !cancellationToken.IsCancellationRequested)
        {
            var (itemId, parentPath) = queue.Dequeue();

            var children = await ExecuteWithRetryAsync(
                async () => await _graphClient.Drives[_driveId].Items[itemId].Children
                    .GetAsync(requestConfiguration =>
                    {
                        requestConfiguration.QueryParameters.Select = new[]
                        {
                            "id", "name", "size", "folder", "file",
                            "createdDateTime", "lastModifiedDateTime",
                            "webUrl", "eTag", "cTag", "hashes"
                        };
                    }, cancellationToken: cancellationToken),
                cancellationToken);

            if (children?.Value == null) continue;

            foreach (var item in children.Value)
            {
                if (item.Name == null) continue;

                var relativePath = string.IsNullOrEmpty(parentPath)
                    ? item.Name
                    : $"{parentPath}/{item.Name}";

                var metadata = BuildMetadata(item, relativePath);

                if (item.Folder != null)
                {
                    // 目录项
                    yield return new FileSystemSyncItem(
                        metadata,
                        SyncItemType.Directory,
                        streamReaderFactory: _ => throw new InvalidOperationException("目录不支持读取流"),
                        childrenFactory: ct => GetChildrenAsync(item.Id!, relativePath, ct));

                    // 如果是目录，加入队列继续遍历
                    if (item.Id != null)
                    {
                        queue.Enqueue((item.Id, relativePath));
                    }
                }
                else
                {
                    // 文件项
                    yield return new FileSystemSyncItem(
                        metadata,
                        SyncItemType.File,
                        streamReaderFactory: ct => OpenReadAsync(item.Id!, ct),
                        streamWriterFactory: ct => OpenWriteAsync(item.Id!, metadata.Path, ct));
                }
            }
        }
    }

    public async Task UploadAsync(ISyncItem item, CancellationToken cancellationToken)
    {
        if (State != NodeState.Connected)
        {
            throw new InvalidOperationException("节点未连接");
        }

        ArgumentNullException.ThrowIfNull(item);

        var targetPath = NormalizeRelativePath(item.Metadata.Path);

        if (item.ItemType == SyncItemType.Directory)
        {
            if (!string.IsNullOrWhiteSpace(targetPath))
            {
                await GetOrCreateFolderAsync(targetPath, cancellationToken);
            }
            return;
        }

        var fileName = Path.GetFileName(targetPath);
        var parentPath = Path.GetDirectoryName(targetPath)?.Replace('\\', '/');

        // 确保父目录存在
        string? parentItemId = null;
        if (!string.IsNullOrEmpty(parentPath))
        {
            var parentFolder = await GetOrCreateFolderAsync(parentPath, cancellationToken);
            parentItemId = parentFolder?.Id;
        }

        // 获取文件大小
        var fileSize = item.Metadata.Size;

        // 根据文件大小选择上传策略
        if (fileSize <= _options.LargeFileThresholdBytes)
        {
            await UploadSmallFileAsync(item, parentItemId, fileName, cancellationToken);
        }
        else
        {
            await UploadLargeFileAsync(item, parentItemId, fileName, fileSize, cancellationToken);
        }

        _logger.LogInformation("已上传文件到 OneDrive: {Path}", targetPath);
    }

    public async Task DownloadAsync(ISyncItem item, CancellationToken cancellationToken)
    {
        // OneDrive 的下载通过 ISyncItem.OpenReadAsync 实现
        _logger.LogDebug("下载 OneDrive 文件: {Path}", item.Metadata.Path);
        await Task.CompletedTask;
    }

    public async Task DeleteAsync(string relativePath, CancellationToken cancellationToken)
    {
        if (State != NodeState.Connected)
        {
            throw new InvalidOperationException("节点未连接");
        }

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new InvalidOperationException("删除目标路径不能为空；如需删除配置根目录，请显式调用 DeleteConfiguredRootAsync。");
        }

        var targetPath = NormalizeRelativePath(relativePath);
        
        try
        {
            // 查找要删除的项
            var item = await FindItemRelativeToConnectedRootAsync(targetPath, cancellationToken);
            if (item?.Id == null)
            {
                _logger.LogWarning("要删除的 OneDrive 项不存在: {Path}", targetPath);
                return;
            }

            await ExecuteWithRetryAsync(
                async () => { await _graphClient.Drives[_driveId].Items[item.Id].DeleteAsync(cancellationToken: cancellationToken); return Task.CompletedTask; },
                cancellationToken);

            _logger.LogInformation("已删除 OneDrive 项: {Path}", targetPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除 OneDrive 项失败: {Path}", targetPath);
            throw;
        }
    }

    /// <summary>
    /// 显式删除当前配置的 RootPath 本身。
    /// 仅用于 OneDrive 集成测试的专用测试目录清理，避免 DeleteAsync(empty) 产生误删语义。
    /// </summary>
    public async Task DeleteConfiguredRootAsync(CancellationToken cancellationToken)
    {
        if (State != NodeState.Connected)
        {
            throw new InvalidOperationException("节点未连接");
        }

        var configuredRootPath = NormalizePath(_options.RootPath);
        if (string.IsNullOrWhiteSpace(configuredRootPath))
        {
            throw new InvalidOperationException("当前节点未配置可删除的 RootPath。");
        }

        try
        {
            var item = await FindItemByPathAsync(configuredRootPath, cancellationToken);
            if (item?.Id == null)
            {
                _logger.LogWarning("要删除的 OneDrive 根目录不存在: {Path}", configuredRootPath);
                return;
            }

            await ExecuteWithRetryAsync(
                async () => { await _graphClient.Drives[_driveId].Items[item.Id].DeleteAsync(cancellationToken: cancellationToken); return Task.CompletedTask; },
                cancellationToken);

            _logger.LogInformation("已删除 OneDrive 配置根目录: {Path}", configuredRootPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除 OneDrive 配置根目录失败: {Path}", configuredRootPath);
            throw;
        }
    }

    private async Task<Stream> OpenReadAsync(string itemId, CancellationToken cancellationToken)
    {
        return await ExecuteWithRetryAsync(
            async () =>
            {
                var stream = await _graphClient.Drives[_driveId].Items[itemId].Content.GetAsync(cancellationToken: cancellationToken);
                if (stream == null)
                {
                    throw new InvalidOperationException("无法读取文件内容");
                }
                return stream;
            },
            cancellationToken);
    }

    private async Task<Stream> OpenWriteAsync(string itemId, string path, CancellationToken cancellationToken)
    {
        // 远程文件不支持直接写入流，需要先下载到内存或临时文件
        // 这里返回一个 MemoryStream，实际写入在 UploadAsync 中处理
        throw new NotSupportedException("远程文件不支持直接打开写入流，请使用 UploadAsync 方法。");
    }

    private async IAsyncEnumerable<ISyncItem> GetChildrenAsync(
        string itemId, 
        string parentPath, 
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var children = await ExecuteWithRetryAsync<DriveItemCollectionResponse?>(
            async () => await _graphClient.Drives[_driveId].Items[itemId].Children
                .GetAsync(requestConfiguration =>
                {
                    requestConfiguration.QueryParameters.Select = new[]
                    {
                        "id", "name", "size", "folder", "file",
                        "createdDateTime", "lastModifiedDateTime",
                        "webUrl", "eTag", "cTag", "hashes"
                    };
                }, cancellationToken: cancellationToken),
            cancellationToken);

        if (children?.Value == null)
        {
            yield break;
        }

        foreach (var item in children.Value)
        {
            if (item.Name == null) continue;

            var relativePath = $"{parentPath}/{item.Name}";
            var metadata = BuildMetadata(item, relativePath);

            if (item.Folder != null)
            {
                yield return new FileSystemSyncItem(
                    metadata,
                    SyncItemType.Directory,
                    streamReaderFactory: _ => throw new InvalidOperationException("目录不支持读取流"),
                    childrenFactory: ct => GetChildrenAsync(item.Id!, relativePath, ct));
            }
            else
            {
                yield return new FileSystemSyncItem(
                    metadata,
                    SyncItemType.File,
                    streamReaderFactory: ct => OpenReadAsync(item.Id!, ct),
                    streamWriterFactory: ct => OpenWriteAsync(item.Id!, metadata.Path, ct));
            }
        }
    }

    private async Task UploadSmallFileAsync(ISyncItem item, string? parentItemId, string fileName, CancellationToken cancellationToken)
    {
        await using var stream = await item.OpenReadAsync(cancellationToken);

        DriveItem? uploadedItem = await ExecuteWithRetryAsync(
            async () =>
            {
                if (!string.IsNullOrEmpty(parentItemId))
                {
                    return await _graphClient.Drives[_driveId].Items[parentItemId].ItemWithPath(fileName).Content
                        .PutAsync(stream, cancellationToken: cancellationToken);
                }
                else
                {
                    return await _graphClient.Drives[_driveId].Items[_rootItemId].ItemWithPath(fileName).Content
                        .PutAsync(stream, cancellationToken: cancellationToken);
                }
            },
            cancellationToken);

        if (uploadedItem == null)
        {
            throw new InvalidOperationException("文件上传失败");
        }
    }

    private async Task UploadLargeFileAsync(ISyncItem item, string? parentItemId, string fileName, long fileSize, CancellationToken cancellationToken)
    {
        // 创建上传会话
        var uploadSessionRequestBody = new Microsoft.Graph.Drives.Item.Items.Item.CreateUploadSession.CreateUploadSessionPostRequestBody
        {
            Item = new DriveItemUploadableProperties
            {
                AdditionalData = new Dictionary<string, object>
                {
                    { "@microsoft.graph.conflictBehavior", _options.ConflictBehavior }
                }
            }
        };

        UploadSession? uploadSession = await ExecuteWithRetryAsync(
            async () =>
            {
                if (!string.IsNullOrEmpty(parentItemId))
                {
                    return await _graphClient.Drives[_driveId].Items[parentItemId].ItemWithPath(fileName).CreateUploadSession
                        .PostAsync(uploadSessionRequestBody, cancellationToken: cancellationToken);
                }
                else
                {
                    return await _graphClient.Drives[_driveId].Items[_rootItemId].ItemWithPath(fileName).CreateUploadSession
                        .PostAsync(uploadSessionRequestBody, cancellationToken: cancellationToken);
                }
            },
            cancellationToken);

        if (uploadSession == null)
        {
            throw new InvalidOperationException("创建上传会话失败");
        }

        // 执行分块上传
        await using var stream = await item.OpenReadAsync(cancellationToken);
        
        var uploadTask = new LargeFileUploadTask<DriveItem>(
            uploadSession,
            stream,
            _options.UploadSliceSizeBytes);

        // 添加上传进度报告
        var progress = new Progress<long>(bytesUploaded =>
        {
            var percentage = fileSize > 0 ? (double)bytesUploaded / fileSize * 100 : 0;
            _logger.LogDebug("上传进度: {Percentage:F1}% ({BytesUploaded}/{TotalBytes})", 
                percentage, bytesUploaded, fileSize);
        });

        var result = await uploadTask.UploadAsync(progress: progress, cancellationToken: cancellationToken);

        if (result.UploadSucceeded)
        {
            _logger.LogInformation("大文件上传成功: {FileName}", fileName);
        }
        else
        {
            throw new InvalidOperationException($"大文件上传失败: {fileName}");
        }
    }

    private async Task<DriveItem?> GetOrCreateFolderAsync(string path, CancellationToken cancellationToken)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var currentItemId = _rootItemId;

        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(currentItemId)) break;

            try
            {
                // 尝试查找子文件夹
                // 注意：个人 OneDrive 不支持 $filter 查询参数，需要获取所有子项后过滤
                var children = await ExecuteWithRetryAsync(
                    async () => await _graphClient.Drives[_driveId].Items[currentItemId].Children
                        .GetAsync(requestConfiguration =>
                        {
                            requestConfiguration.QueryParameters.Select = new[] { "id", "name", "folder" };
                        }, cancellationToken: cancellationToken),
                    cancellationToken);

                // 在内存中查找匹配的文件夹（个人 OneDrive 不支持服务器端过滤）
                var existingFolder = children?.Value?.FirstOrDefault(c => 
                    c.Name == part && c.Folder != null);

                if (existingFolder?.Id != null)
                {
                    currentItemId = existingFolder.Id;
                    _logger.LogDebug("找到现有文件夹: {FolderName}, ID: {FolderId}", part, currentItemId);
                }
                else
                {
                    // 创建新文件夹
                    _logger.LogInformation("创建新文件夹: {FolderName}", part);
                    var newFolder = new DriveItem
                    {
                        Name = part,
                        Folder = new Folder()
                    };

                    var createdFolder = await ExecuteWithRetryAsync(
                        async () => await _graphClient.Drives[_driveId].Items[currentItemId].Children
                            .PostAsync(newFolder, cancellationToken: cancellationToken),
                        cancellationToken);

                    currentItemId = createdFolder?.Id;
                    _logger.LogInformation("文件夹创建成功: {FolderName}, ID: {FolderId}", part, currentItemId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建文件夹失败: {FolderName}", part);
                throw;
            }
        }

        if (string.IsNullOrEmpty(currentItemId))
        {
            throw new InvalidOperationException($"无法获取或创建文件夹: {path}");
        }

        return new DriveItem { Id = currentItemId };
    }

    private async Task<DriveItem?> FindItemByPathAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var normalizedPath = path.Replace('\\', '/').TrimStart('/');
            return await ExecuteWithRetryAsync(
                async () => await _graphClient.Drives[_driveId].Root.ItemWithPath(normalizedPath).GetAsync(cancellationToken: cancellationToken),
                cancellationToken);
        }
        catch (Microsoft.Graph.Models.ODataErrors.ODataError ex) when (ex.ResponseStatusCode == 404)
        {
            return null;
        }
    }

    private async Task<DriveItem?> FindItemRelativeToConnectedRootAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var normalizedPath = NormalizeRelativePath(path);
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                return new DriveItem { Id = _rootItemId };
            }

            return await ExecuteWithRetryAsync(
                async () => await _graphClient.Drives[_driveId].Items[_rootItemId].ItemWithPath(normalizedPath).GetAsync(cancellationToken: cancellationToken),
                cancellationToken);
        }
        catch (Microsoft.Graph.Models.ODataErrors.ODataError ex) when (ex.ResponseStatusCode == 404)
        {
            return null;
        }
    }

    private string CombinePath(string? rootPath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return NormalizePath(relativePath);
        }

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return NormalizePath(rootPath);
        }

        return $"{NormalizePath(rootPath)}/{NormalizePath(relativePath)}";
    }

    private static string NormalizePath(string? path)
    {
        return (path ?? string.Empty).Replace('\\', '/').Trim('/');
    }

    private SyncItemMetadata BuildMetadata(DriveItem driveItem, string relativePath)
    {
        var name = driveItem.Name ?? Path.GetFileName(relativePath);
        var size = driveItem.Size ?? 0;
        var createdAt = driveItem.CreatedDateTime?.UtcDateTime;
        var modifiedAt = driveItem.LastModifiedDateTime?.UtcDateTime;

        // 使用 OneDrive 提供的哈希值作为校验和（优先 QuickXorHash）
        string? checksum = null;
        if (driveItem.File?.Hashes?.QuickXorHash != null)
        {
            checksum = driveItem.File.Hashes.QuickXorHash;
        }
        else if (driveItem.File?.Hashes?.Sha1Hash != null)
        {
            checksum = driveItem.File.Hashes.Sha1Hash;
        }
        else if (driveItem.File?.Hashes?.Sha256Hash != null)
        {
            checksum = driveItem.File.Hashes.Sha256Hash;
        }

        var contentType = driveItem.Folder != null
            ? "inode/directory"
            : (driveItem.File?.MimeType ?? "application/octet-stream");

        return new SyncItemMetadata(
            relativePath,
            name,
            NormalizeRelativePath(relativePath),
            size,
            createdAt,
            modifiedAt,
            checksum,
            contentType);
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        return relativePath.Replace('\\', '/').TrimStart('/');
    }

    /// <summary>
    /// 执行带重试的异步操作。
    /// 支持速率限制（429）的指数退避重试。
    /// </summary>
    private async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < _options.MaxRetries; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (Microsoft.Graph.Models.ODataErrors.ODataError ex) 
                when (ex.ResponseStatusCode == (int)HttpStatusCode.TooManyRequests)
            {
                if (attempt == _options.MaxRetries - 1)
                {
                    throw;
                }

                var delay = TimeSpan.FromMilliseconds(_options.BackoffMs * Math.Pow(2, attempt));
                _logger.LogWarning("遇到 OneDrive 速率限制，等待 {DelayMs}ms 后重试 (尝试 {Attempt}/{MaxRetries})", 
                    delay.TotalMilliseconds, attempt + 1, _options.MaxRetries);
                
                await Task.Delay(delay, cancellationToken);
            }
        }

        throw new InvalidOperationException("重试次数耗尽");
    }
}

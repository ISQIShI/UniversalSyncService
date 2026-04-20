using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using UniversalSyncService.Abstractions.SyncItems;

namespace UniversalSyncService.Core.SyncItems;

/// <summary>
/// 普通文件系统同步对象。
/// 这里把物理文件/目录包装成统一的同步项，供同步引擎读取元数据和流内容。
/// 支持通过工厂函数延迟加载流，可用于任意存储后端（本地、云存储等）。
/// </summary>
public sealed class FileSystemSyncItem : ISyncItem
{
    private readonly string? _absolutePath;
    private readonly Func<CancellationToken, Task<Stream>>? _streamReaderFactory;
    private readonly Func<CancellationToken, Task<Stream>>? _streamWriterFactory;
    private readonly Func<CancellationToken, IAsyncEnumerable<ISyncItem>>? _childrenFactory;

    /// <summary>
    /// 基于本地文件路径构造函数（用于本地节点）。
    /// 此构造函数会直接绑定到本地文件系统的特定路径。
    /// </summary>
    /// <param name="absolutePath">文件或目录的绝对路径。</param>
    /// <param name="relativePath">相对于同步根目录的相对路径。</param>
    public FileSystemSyncItem(string absolutePath, string relativePath)
    {
        ArgumentNullException.ThrowIfNull(absolutePath);
        ArgumentNullException.ThrowIfNull(relativePath);

        _absolutePath = absolutePath;
        ItemType = Directory.Exists(absolutePath)
            ? SyncItemType.Directory
            : SyncItemType.File;

        Metadata = BuildMetadataFromFilePath(absolutePath, relativePath, ItemType);
    }

    /// <summary>
    /// 基于工厂函数构造函数（支持任意存储后端）。
    /// 此构造函数允许通过工厂函数延迟加载流内容，适用于云存储、网络存储等场景。
    /// </summary>
    /// <param name="metadata">同步项元数据。</param>
    /// <param name="itemType">同步项类型（文件或目录）。</param>
    /// <param name="streamReaderFactory">流读取工厂函数，用于延迟加载文件内容。</param>
    /// <param name="streamWriterFactory">流写入工厂函数，用于延迟写入文件内容。</param>
    /// <param name="childrenFactory">子项枚举工厂函数，用于延迟加载目录内容。</param>
    public FileSystemSyncItem(
        SyncItemMetadata metadata,
        SyncItemType itemType,
        Func<CancellationToken, Task<Stream>>? streamReaderFactory = null,
        Func<CancellationToken, Task<Stream>>? streamWriterFactory = null,
        Func<CancellationToken, IAsyncEnumerable<ISyncItem>>? childrenFactory = null)
    {
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        ItemType = itemType;
        _streamReaderFactory = streamReaderFactory;
        _streamWriterFactory = streamWriterFactory;
        _childrenFactory = childrenFactory;
    }

    public string Kind => SyncItemKinds.FileSystem;

    public SyncItemMetadata Metadata { get; }

    public string Identity => Metadata.Id;

    public SyncItemType ItemType { get; }

    public bool SupportsCapability(SyncItemCapabilities capability)
    {
        var capabilities = ResolveCapabilities();
        return (capabilities & capability) == capability;
    }

    /// <summary>
    /// 打开读取流。
    /// 如果提供了流读取工厂，则使用工厂；否则从本地文件路径读取。
    /// </summary>
    public Task<Stream> OpenReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (ItemType != SyncItemType.File)
        {
            throw new InvalidOperationException("目录同步项不支持直接打开读取流。");
        }

        if (_streamReaderFactory != null)
        {
            return _streamReaderFactory(cancellationToken);
        }

        if (_absolutePath == null)
        {
            throw new InvalidOperationException("未提供流读取工厂或本地文件路径。");
        }

        Stream stream = new FileStream(_absolutePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Task.FromResult(stream);
    }

    /// <summary>
    /// 打开写入流。
    /// 如果提供了流写入工厂，则使用工厂；否则写入本地文件路径。
    /// </summary>
    public Task<Stream> OpenWriteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (ItemType != SyncItemType.File)
        {
            throw new InvalidOperationException("目录同步项不支持直接打开写入流。");
        }

        if (_streamWriterFactory != null)
        {
            return _streamWriterFactory(cancellationToken);
        }

        if (_absolutePath == null)
        {
            throw new InvalidOperationException("未提供流写入工厂或本地文件路径。");
        }

        var directory = Path.GetDirectoryName(_absolutePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        Stream stream = new FileStream(_absolutePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        return Task.FromResult(stream);
    }

    /// <summary>
    /// 获取子项列表。
    /// 如果提供了子项工厂，则使用工厂；否则从本地目录枚举。
    /// </summary>
    public async IAsyncEnumerable<ISyncItem> GetChildrenAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (ItemType != SyncItemType.Directory)
        {
            yield break;
        }

        if (_childrenFactory != null)
        {
            await foreach (var child in _childrenFactory(cancellationToken).WithCancellation(cancellationToken))
            {
                yield return child;
            }
            yield break;
        }

        if (_absolutePath == null)
        {
            yield break;
        }

        // 使用本地文件路径创建子项
        await foreach (var child in CreateChildrenFromLocalPathAsync(_absolutePath, Metadata.Path, cancellationToken))
        {
            yield return child;
        }
    }

    /// <summary>
    /// 从本地目录路径创建子项枚举。
    /// </summary>
    private static async IAsyncEnumerable<FileSystemSyncItem> CreateChildrenFromLocalPathAsync(
        string parentAbsolutePath,
        string parentRelativePath,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // 枚举子目录
        foreach (var directory in Directory.EnumerateDirectories(parentAbsolutePath))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var directoryName = Path.GetFileName(directory);
            var relativePath = CombineRelativePath(parentRelativePath, directoryName);

            yield return new FileSystemSyncItem(directory, relativePath);
        }

        // 枚举文件
        foreach (var file in Directory.EnumerateFiles(parentAbsolutePath))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fileName = Path.GetFileName(file);
            var relativePath = CombineRelativePath(parentRelativePath, fileName);

            yield return new FileSystemSyncItem(file, relativePath);
        }

        await Task.CompletedTask; // 满足 async 方法要求
    }

    /// <summary>
    /// 获取校验和。
    /// 如果元数据中已有校验和，直接返回；否则从本地文件计算。
    /// </summary>
    public async Task<string?> GetChecksumAsync(string algorithm, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (ItemType != SyncItemType.File)
        {
            return null;
        }

        // 如果元数据中已有校验和，直接返回
        if (!string.IsNullOrEmpty(Metadata.Checksum))
        {
            return Metadata.Checksum;
        }

        if (_absolutePath == null)
        {
            return null;
        }

        await using var stream = new FileStream(_absolutePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        byte[] hash = algorithm.ToUpperInvariant() switch
        {
            "MD5" => await MD5.HashDataAsync(stream, cancellationToken),
            _ => await SHA256.HashDataAsync(stream, cancellationToken)
        };

        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// 获取扩展元数据。
    /// </summary>
    public Task<IDictionary<string, object>> GetExtendedMetadataAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var extendedMetadata = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        if (_absolutePath != null)
        {
            extendedMetadata["AbsolutePath"] = _absolutePath;
            extendedMetadata["Exists"] = File.Exists(_absolutePath) || Directory.Exists(_absolutePath);
        }

        return Task.FromResult((IDictionary<string, object>)extendedMetadata);
    }

    private SyncItemCapabilities ResolveCapabilities()
    {
        var capabilities = SyncItemCapabilities.CanProvideExtendedMetadata;

        if (ItemType == SyncItemType.File)
        {
            capabilities |= SyncItemCapabilities.CanReadContent
                | SyncItemCapabilities.CanComputeChecksum;

            if (_streamWriterFactory is not null || _absolutePath is not null)
            {
                capabilities |= SyncItemCapabilities.CanWriteContent;
            }
        }

        if (ItemType is SyncItemType.Directory or SyncItemType.Container)
        {
            capabilities |= SyncItemCapabilities.CanEnumerateChildren;
        }

        return capabilities;
    }

    /// <summary>
    /// 从本地文件路径构建元数据。
    /// </summary>
    private static SyncItemMetadata BuildMetadataFromFilePath(string absolutePath, string relativePath, SyncItemType itemType)
    {
        if (itemType == SyncItemType.Directory)
        {
            var directoryInfo = new DirectoryInfo(absolutePath);
            return new SyncItemMetadata(
                relativePath,
                directoryInfo.Name,
                NormalizeRelativePath(relativePath),
                0,
                directoryInfo.CreationTimeUtc,
                directoryInfo.LastWriteTimeUtc,
                checksum: null,
                contentType: "inode/directory");
        }

        var fileInfo = new FileInfo(absolutePath);
        // 这里在元数据阶段就计算内容摘要，避免"文件大小没变但内容已改"的情况被误判成未修改。
        var checksum = ComputeChecksum(absolutePath);
        return new SyncItemMetadata(
            relativePath,
            fileInfo.Name,
            NormalizeRelativePath(relativePath),
            fileInfo.Length,
            fileInfo.CreationTimeUtc,
            fileInfo.LastWriteTimeUtc,
            checksum: checksum,
            contentType: "application/octet-stream");
    }

    /// <summary>
    /// 计算文件的 SHA256 校验和。
    /// </summary>
    private static string ComputeChecksum(string absolutePath)
    {
        using var stream = new FileStream(
            absolutePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// 规范化相对路径（统一使用正斜杠，去除前导斜杠）。
    /// </summary>
    private static string NormalizeRelativePath(string relativePath)
    {
        return relativePath.Replace("\\", "/").TrimStart('/');
    }

    /// <summary>
    /// 组合相对路径。
    /// </summary>
    private static string CombineRelativePath(string parentPath, string childName)
    {
        if (string.IsNullOrEmpty(parentPath))
        {
            return childName;
        }
        return $"{parentPath.Replace("\\", "/").TrimEnd('/')}/{childName}";
    }
}

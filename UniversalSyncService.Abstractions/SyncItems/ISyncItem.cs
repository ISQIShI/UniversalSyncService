namespace UniversalSyncService.Abstractions.SyncItems;

/// <summary>
/// 表示可同步项的契约。
/// </summary>
public interface ISyncItem
{
    /// <summary>
    /// 获取同步项的元数据。
    /// </summary>
    SyncItemMetadata Metadata { get; }

    /// <summary>
    /// 获取同步项的类型。
    /// </summary>
    SyncItemType ItemType { get; }

    /// <summary>
    /// 打开读取流。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>用于读取的流。</returns>
    Task<Stream> OpenReadAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 打开写入流。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>用于写入的流。</returns>
    Task<Stream> OpenWriteAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 获取子项列表（仅对目录或容器类型有效）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>子项的异步枚举。</returns>
    IAsyncEnumerable<ISyncItem> GetChildrenAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 获取校验和。
    /// </summary>
    /// <param name="algorithm">校验算法（如 "SHA256", "MD5"）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>校验和字符串。</returns>
    Task<string?> GetChecksumAsync(string algorithm, CancellationToken cancellationToken);

    /// <summary>
    /// 获取扩展元数据。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>扩展元数据字典。</returns>
    Task<IDictionary<string, object>> GetExtendedMetadataAsync(CancellationToken cancellationToken);
}

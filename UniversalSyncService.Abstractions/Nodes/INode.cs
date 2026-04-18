namespace UniversalSyncService.Abstractions.Nodes;

using UniversalSyncService.Abstractions.SyncItems;

/// <summary>
/// 表示一个可参与同步的节点契约。
/// </summary>
public interface INode
{
    /// <summary>
    /// 获取节点元数据。
    /// </summary>
    NodeMetadata Metadata { get; }

    /// <summary>
    /// 获取节点能力。
    /// </summary>
    NodeCapabilities Capabilities { get; }

    /// <summary>
    /// 获取节点当前状态。
    /// </summary>
    NodeState State { get; }

    /// <summary>
    /// 连接节点。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task ConnectAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 断开节点连接。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    Task DisconnectAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 获取可同步项列表。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>可同步项的异步序列。</returns>
    IAsyncEnumerable<ISyncItem> GetSyncItemsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 上传同步项到节点。
    /// </summary>
    /// <param name="item">要上传的同步项。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task UploadAsync(ISyncItem item, CancellationToken cancellationToken);

    /// <summary>
    /// 从节点下载同步项。
    /// </summary>
    /// <param name="item">要下载的同步项。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task DownloadAsync(ISyncItem item, CancellationToken cancellationToken);

    /// <summary>
    /// 删除节点上的同步项。
    /// </summary>
    /// <param name="relativePath">要删除的相对路径。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task DeleteAsync(string relativePath, CancellationToken cancellationToken);
}

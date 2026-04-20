namespace UniversalSyncService.Abstractions.SyncItems;

/// <summary>
/// 表示同步项工厂，用于创建不同来源的同步项。
/// </summary>
public interface ISyncItemFactory
{
    /// <summary>
    /// 获取工厂可创建的同步项能力类型。
    /// </summary>
    string SyncItemKind { get; }

    /// <summary>
    /// 判断工厂是否支持指定同步对象能力类型。
    /// </summary>
    /// <param name="syncItemKind">同步对象能力类型。</param>
    /// <returns>如果支持则返回 <see langword="true"/>，否则返回 <see langword="false"/>。</returns>
    bool SupportsKind(string syncItemKind);

    /// <summary>
    /// 基于对象身份标识创建同步项。
    /// </summary>
    /// <param name="identity">对象身份标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>创建的同步项。</returns>
    Task<ISyncItem> CreateFromIdentityAsync(string identity, CancellationToken cancellationToken);

    /// <summary>
    /// 从流与对象身份创建同步项。
    /// </summary>
    /// <param name="stream">输入流。</param>
    /// <param name="identity">对象身份标识。</param>
    /// <param name="metadata">同步项元数据。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>创建的同步项。</returns>
    Task<ISyncItem> CreateFromStreamAsync(Stream stream, string identity, SyncItemMetadata metadata, CancellationToken cancellationToken);
}

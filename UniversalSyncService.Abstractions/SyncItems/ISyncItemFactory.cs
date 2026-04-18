using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace UniversalSyncService.Abstractions.SyncItems;

/// <summary>
/// 表示同步项工厂，用于创建不同来源的同步项。
/// </summary>
public interface ISyncItemFactory
{
    /// <summary>
    /// 从文件系统路径创建同步项。
    /// </summary>
    /// <param name="path">文件系统路径。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>创建的同步项。</returns>
    Task<ISyncItem> CreateFromPathAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// 从流创建同步项。
    /// </summary>
    /// <param name="stream">输入流。</param>
    /// <param name="metadata">同步项元数据。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>创建的同步项。</returns>
    Task<ISyncItem> CreateFromStreamAsync(Stream stream, SyncItemMetadata metadata, CancellationToken cancellationToken);

    /// <summary>
    /// 判断工厂是否支持指定路径。
    /// </summary>
    /// <param name="path">文件系统路径。</param>
    /// <returns>如果支持则返回 <see langword="true"/>，否则返回 <see langword="false"/>。</returns>
    bool SupportsPath(string path);
}

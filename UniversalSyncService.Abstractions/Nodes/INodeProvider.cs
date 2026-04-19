using UniversalSyncService.Abstractions.SyncManagement.ConfigNodes;

namespace UniversalSyncService.Abstractions.Nodes;

/// <summary>
/// 表示节点提供程序契约。
/// </summary>
public interface INodeProvider
{
    /// <summary>
    /// 获取节点提供程序标识。
    /// </summary>
    string ProviderType { get; }

    /// <summary>
    /// 判断是否可以基于给定配置创建节点。
    /// </summary>
    /// <param name="configuration">节点配置对象。</param>
    /// <returns>如果支持该配置，则返回 <c>true</c>；否则返回 <c>false</c>。</returns>
    bool CanCreate(NodeConfiguration configuration);

    /// <summary>
    /// 判断当前 Provider 是否支持给定同步对象能力类型。
    /// </summary>
    bool SupportsSyncItemKind(string syncItemKind);

    /// <summary>
    /// 创建节点实例。
    /// </summary>
    /// <param name="configuration">节点配置对象。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>创建的节点实例。</returns>
    Task<INode> CreateAsync(NodeConfiguration configuration, CancellationToken cancellationToken);

    /// <summary>
    /// 归一化节点配置，补齐当前 Provider 需要的默认值或派生值。
    /// </summary>
    NodeConfiguration NormalizeConfiguration(NodeConfiguration configuration);

    /// <summary>
    /// 验证节点配置是否有效。
    /// </summary>
    (bool IsValid, string? ErrorMessage) ValidateConfiguration(NodeConfiguration configuration);

    /// <summary>
    /// 在节点保存时执行 Provider 所需的预认证或前置校验。
    /// </summary>
    Task EnsureAuthenticatedAsync(NodeConfiguration configuration, CancellationToken cancellationToken);

    /// <summary>
    /// 判断当前节点类型是否支持在计划中使用绝对路径。
    /// </summary>
    bool SupportsAbsoluteScopedPath(NodeConfiguration configuration);

    /// <summary>
    /// 将计划中的 scopedPath 解析为当前节点最终使用的根路径语义。
    /// </summary>
    string ResolveScopedRoot(NodeConfiguration configuration, string? scopedPath);

    /// <summary>
    /// 获取用于 UI/HTTP API 展示的节点根路径。
    /// </summary>
    string? GetDisplayRootPath(NodeConfiguration configuration);
}

using UniversalSyncService.Abstractions.Nodes;
using UniversalSyncService.Abstractions.SyncManagement.ConfigNodes;

namespace UniversalSyncService.Core.Providers;

/// <summary>
/// 统一管理节点提供程序，避免任务层直接遍历全部 Provider。
/// </summary>
public sealed class NodeProviderRegistry
{
    private readonly IReadOnlyList<INodeProvider> _providers;

    public NodeProviderRegistry(IEnumerable<INodeProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = providers.ToList();
    }

    /// <summary>
    /// 判断当前配置是否存在可用的节点提供程序。
    /// </summary>
    public bool CanCreate(NodeConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return _providers.Any(provider => provider.CanCreate(configuration));
    }

    /// <summary>
    /// 判断指定节点配置是否支持给定同步对象能力类型。
    /// </summary>
    public bool SupportsSyncItemKind(NodeConfiguration configuration, string syncItemKind)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(syncItemKind);

        return ResolveProvider(configuration).SupportsSyncItemKind(syncItemKind);
    }

    /// <summary>
    /// 基于节点配置创建运行时节点实例。
    /// </summary>
    public async Task<INode> CreateAsync(NodeConfiguration configuration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var provider = ResolveProvider(configuration);
        return await provider.CreateAsync(configuration, cancellationToken);
    }

    public NodeConfiguration NormalizeConfiguration(NodeConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return ResolveProvider(configuration).NormalizeConfiguration(configuration);
    }

    public (bool IsValid, string? ErrorMessage) ValidateConfiguration(NodeConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return ResolveProvider(configuration).ValidateConfiguration(configuration);
    }

    public Task EnsureAuthenticatedAsync(NodeConfiguration configuration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return ResolveProvider(configuration).EnsureAuthenticatedAsync(configuration, cancellationToken);
    }

    public bool SupportsAbsoluteScopedPath(NodeConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return ResolveProvider(configuration).SupportsAbsoluteScopedPath(configuration);
    }

    public string ResolveScopedRoot(NodeConfiguration configuration, string? scopedPath)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return ResolveProvider(configuration).ResolveScopedRoot(configuration, scopedPath);
    }

    public string? GetDisplayRootPath(NodeConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return ResolveProvider(configuration).GetDisplayRootPath(configuration);
    }

    private INodeProvider ResolveProvider(NodeConfiguration configuration)
    {
        var provider = _providers.FirstOrDefault(candidate => candidate.CanCreate(configuration));
        if (provider is null)
        {
            throw new InvalidOperationException($"未找到可用于节点 {configuration.Id} 的 Provider。");
        }

        return provider;
    }
}

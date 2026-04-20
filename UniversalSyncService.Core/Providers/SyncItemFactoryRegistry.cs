using UniversalSyncService.Abstractions.SyncItems;

namespace UniversalSyncService.Core.Providers;

/// <summary>
/// 统一管理同步对象工厂，便于后续按路径或类型扩展更多实现。
/// </summary>
public sealed class SyncItemFactoryRegistry
{
    private readonly IReadOnlyList<ISyncItemFactory> _factories;

    public SyncItemFactoryRegistry(IEnumerable<ISyncItemFactory> factories)
    {
        ArgumentNullException.ThrowIfNull(factories);
        _factories = factories.ToList();
    }

    /// <summary>
    /// 判断同步对象能力类型是否存在可用工厂。
    /// </summary>
    public bool SupportsKind(string syncItemKind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(syncItemKind);
        return _factories.Any(factory => factory.SupportsKind(syncItemKind));
    }

    /// <summary>
    /// 按对象身份创建同步对象。
    /// </summary>
    public async Task<ISyncItem> CreateFromIdentityAsync(string syncItemKind, string identity, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(syncItemKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);

        var factory = _factories.FirstOrDefault(candidate => candidate.SupportsKind(syncItemKind));
        if (factory is null)
        {
            throw new InvalidOperationException($"未找到可用于能力类型 {syncItemKind} 的同步对象工厂。");
        }

        return await factory.CreateFromIdentityAsync(identity, cancellationToken);
    }
}

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
    /// 判断路径是否存在可用工厂。
    /// </summary>
    public bool SupportsPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return _factories.Any(factory => factory.SupportsPath(path));
    }

    /// <summary>
    /// 按路径创建同步对象。
    /// </summary>
    public async Task<ISyncItem> CreateFromPathAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(path);

        var factory = _factories.FirstOrDefault(candidate => candidate.SupportsPath(path));
        if (factory is null)
        {
            throw new InvalidOperationException($"未找到可用于路径 {path} 的同步对象工厂。");
        }

        return await factory.CreateFromPathAsync(path, cancellationToken);
    }
}

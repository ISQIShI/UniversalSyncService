using UniversalSyncService.Abstractions.Nodes;
using UniversalSyncService.Abstractions.SyncManagement.ConfigNodes;
using UniversalSyncService.Core.Providers;

namespace UniversalSyncService.Core.Nodes;

/// <summary>
/// 本地节点 Provider。
/// 负责把配置中的 Local 节点转换为可运行的 LocalNode。
/// </summary>
public sealed class LocalNodeProvider : INodeProvider
{
    private readonly SyncItemFactoryRegistry _syncItemFactoryRegistry;

    public LocalNodeProvider(SyncItemFactoryRegistry syncItemFactoryRegistry)
    {
        _syncItemFactoryRegistry = syncItemFactoryRegistry;
    }

    public bool CanCreate(NodeConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return string.Equals(configuration.NodeType, "Local", StringComparison.OrdinalIgnoreCase);
    }

    public Task<INode> CreateAsync(NodeConfiguration configuration, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(configuration);

        if (!configuration.ConnectionSettings.TryGetValue("RootPath", out var rootPath) || string.IsNullOrWhiteSpace(rootPath))
        {
            throw new InvalidOperationException($"节点 {configuration.Id} 缺少 RootPath 配置。");
        }

        var excludedPaths = Array.Empty<string>();
        if (configuration.ConnectionSettings.TryGetValue("ExcludedAbsolutePaths", out var excludedAbsolutePaths)
            && !string.IsNullOrWhiteSpace(excludedAbsolutePaths))
        {
            excludedPaths = excludedAbsolutePaths
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(Path.GetFullPath)
                .ToArray();
        }

        INode node = new LocalNode(
            configuration.Id,
            configuration.Name,
            Path.GetFullPath(rootPath),
            excludedPaths,
            _syncItemFactoryRegistry);

        return Task.FromResult(node);
    }

    public NodeConfiguration NormalizeConfiguration(NodeConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionSettings = new Dictionary<string, string>(configuration.ConnectionSettings, StringComparer.OrdinalIgnoreCase);
        if (connectionSettings.TryGetValue("RootPath", out var rootPath) && !string.IsNullOrWhiteSpace(rootPath))
        {
            connectionSettings["RootPath"] = Path.GetFullPath(rootPath);
        }

        return CloneConfiguration(configuration, connectionSettings);
    }

    public (bool IsValid, string? ErrorMessage) ValidateConfiguration(NodeConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (!configuration.ConnectionSettings.TryGetValue("RootPath", out var rootPath) || string.IsNullOrWhiteSpace(rootPath))
        {
            return (false, "本地节点至少需要提供 RootPath。");
        }

        return (true, null);
    }

    public Task EnsureAuthenticatedAsync(NodeConfiguration configuration, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public bool SupportsAbsoluteScopedPath(NodeConfiguration configuration)
    {
        return CanCreate(configuration);
    }

    public string ResolveScopedRoot(NodeConfiguration configuration, string? scopedPath)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (!configuration.ConnectionSettings.TryGetValue("RootPath", out var rootPath) || string.IsNullOrWhiteSpace(rootPath))
        {
            return string.Empty;
        }

        var normalizedRootPath = Path.GetFullPath(rootPath);
        if (string.IsNullOrWhiteSpace(scopedPath))
        {
            return normalizedRootPath;
        }

        if (Path.IsPathRooted(scopedPath))
        {
            return Path.GetFullPath(scopedPath);
        }

        return Path.GetFullPath(Path.Combine(normalizedRootPath, scopedPath));
    }

    public string? GetDisplayRootPath(NodeConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return configuration.ConnectionSettings.TryGetValue("RootPath", out var rootPath) && !string.IsNullOrWhiteSpace(rootPath)
            ? Path.GetFullPath(rootPath)
            : null;
    }

    private static NodeConfiguration CloneConfiguration(NodeConfiguration source, Dictionary<string, string> connectionSettings)
    {
        return new NodeConfiguration(source.Id, source.Name, source.NodeType, connectionSettings, source.CreatedAt)
        {
            ModifiedAt = source.ModifiedAt,
            IsEnabled = source.IsEnabled,
            CustomOptions = source.CustomOptions is null
                ? null
                : new Dictionary<string, object>(source.CustomOptions, StringComparer.OrdinalIgnoreCase)
        };
    }
}

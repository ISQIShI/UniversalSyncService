using Microsoft.Extensions.Logging;
using UniversalSyncService.Abstractions.Nodes;
using UniversalSyncService.Abstractions.SyncManagement.ConfigNodes;

namespace UniversalSyncService.Core.Nodes.OneDrive;

/// <summary>
/// OneDrive 节点提供者。
/// 负责从配置创建 OneDrive 节点实例。
/// </summary>
public sealed class OneDriveNodeProvider : INodeProvider
{
    private readonly OneDriveGraphClientFactory _clientFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<OneDriveNodeProvider> _logger;

    public OneDriveNodeProvider(
        OneDriveGraphClientFactory clientFactory,
        ILoggerFactory loggerFactory)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = loggerFactory.CreateLogger<OneDriveNodeProvider>();
    }

    public bool CanCreate(NodeConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return configuration.NodeType.Equals("OneDrive", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<INode> CreateAsync(NodeConfiguration configuration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var logger = _loggerFactory.CreateLogger<OneDriveNode>();
        logger.LogInformation("正在创建 OneDrive 节点: {NodeId}", configuration.Id);

        // 从配置解析选项
        var options = CreateOptionsWithDefaults(configuration.ConnectionSettings);

        // 验证配置
        if (!options.Validate(out var errorMessage))
        {
            throw new InvalidOperationException(
                $"OneDrive 配置无效: {errorMessage}\n" +
                $"请确保已通过以下方式之一配置应用程序凭据:\n" +
                $"  1. 在节点配置的 ConnectionSettings 中设置 ClientId\n" +
                $"  2. 运行配置工具: dotnet run --project tools/OneDriveCredentialConfigurator\n" +
                $"  3. 设置环境变量: ONEDRIVE_CLIENT_ID");
        }

        // 创建 Graph 客户端
        var graphClient = await _clientFactory.CreateClientAsync(options, cancellationToken);

        // 创建节点实例
        var node = new OneDriveNode(
            configuration.Id,
            configuration.Name,
            options,
            graphClient,
            logger);

        logger.LogInformation("OneDrive 节点创建成功: {NodeId}", configuration.Id);
        return node;
    }

    public NodeConfiguration NormalizeConfiguration(NodeConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionSettings = new Dictionary<string, string>(configuration.ConnectionSettings, StringComparer.OrdinalIgnoreCase);
        var options = CreateOptionsWithDefaults(connectionSettings);

        connectionSettings["ClientId"] = options.ClientId;
        connectionSettings["TenantId"] = options.TenantId;
        connectionSettings["AuthMode"] = options.AuthMode;
        connectionSettings["RootPath"] = options.RootPath;

        return CloneConfiguration(configuration, connectionSettings);
    }

    public (bool IsValid, string? ErrorMessage) ValidateConfiguration(NodeConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var normalizedConfiguration = NormalizeConfiguration(configuration);
        var options = CreateOptionsWithDefaults(normalizedConfiguration.ConnectionSettings);
        return options.Validate(out var errorMessage)
            ? (true, null)
            : (false, $"OneDrive 节点配置无效：{errorMessage}");
    }

    public Task EnsureAuthenticatedAsync(NodeConfiguration configuration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var normalizedConfiguration = NormalizeConfiguration(configuration);
        var options = CreateOptionsWithDefaults(normalizedConfiguration.ConnectionSettings);
        return _clientFactory.EnsureAuthenticationAsync(options, cancellationToken);
    }

    /// <summary>
    /// WarmAuth 前置检查：要求存在可用的持久化认证记录。
    /// </summary>
    public Task EnsureWarmAuthenticationReadyAsync(NodeConfiguration configuration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var normalizedConfiguration = NormalizeConfiguration(configuration);
        var options = CreateOptionsWithDefaults(normalizedConfiguration.ConnectionSettings);
        return _clientFactory.EnsureWarmAuthenticationReadyAsync(options, cancellationToken);
    }

    public bool SupportsAbsoluteScopedPath(NodeConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return false;
    }

    public string ResolveScopedRoot(NodeConfiguration configuration, string? scopedPath)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = CreateOptionsWithDefaults(configuration.ConnectionSettings);
        var normalizedRoot = NormalizeRemotePath(options.RootPath);
        if (string.IsNullOrWhiteSpace(scopedPath) || string.Equals(scopedPath.Trim(), ".", StringComparison.Ordinal))
        {
            return FormatDisplayRootPath(options.RootPath) ?? "/";
        }

        if (string.Equals(scopedPath.Trim(), "..", StringComparison.Ordinal) || scopedPath.Contains("../", StringComparison.Ordinal) || scopedPath.Contains("..\\", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("OneDrive 节点不支持使用 .. 跳出远端根路径，请改用根路径下的相对路径。");
        }

        if (Path.IsPathRooted(scopedPath) || scopedPath.StartsWith("\\\\", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("OneDrive 节点不支持在计划中使用本机绝对路径，请改用远端相对路径。");
        }

        var normalizedScopedPath = NormalizeRemotePath(scopedPath);
        var resolvedRoot = string.IsNullOrWhiteSpace(normalizedRoot)
            ? normalizedScopedPath
            : $"{normalizedRoot}/{normalizedScopedPath}";
        return string.IsNullOrWhiteSpace(resolvedRoot) ? "/" : $"/{resolvedRoot}";
    }

    public string? GetDisplayRootPath(NodeConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = CreateOptionsWithDefaults(configuration.ConnectionSettings);
        return FormatDisplayRootPath(options.RootPath);
    }

    private OneDriveNodeOptions CreateOptionsWithDefaults(IDictionary<string, string> connectionSettings)
    {
        // 运行时兼容：保留旧环境变量回退能力；测试路径请使用 FromConnectionSettings（无 env fallback）。
        var options = OneDriveNodeOptions.FromConnectionSettingsWithDeprecatedEnvironmentFallback(connectionSettings);

        // 如果配置中未提供 ClientId，尝试从应用程序凭据存储加载。
        if (string.IsNullOrWhiteSpace(options.ClientId))
        {
            var appCredentials = OneDriveAppCredentials.LoadCredentials();
            if (appCredentials != null)
            {
                options.ClientId = appCredentials.ClientId;
                options.TenantId = appCredentials.TenantId;
                _logger.LogDebug("已从应用程序凭据存储加载 ClientId");
            }
        }

        return options;
    }

    private static string NormalizeRemotePath(string? path)
    {
        return (path ?? string.Empty).Replace('\\', '/').Trim('/');
    }

    private static string FormatDisplayRootPath(string? path)
    {
        var normalized = NormalizeRemotePath(path);
        return string.IsNullOrWhiteSpace(normalized) ? "/" : $"/{normalized}";
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

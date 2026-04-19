using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;

namespace UniversalSyncService.Core.Nodes.OneDrive;

/// <summary>
/// OneDrive Graph 客户端工厂。
/// 负责创建和配置 Microsoft Graph 客户端实例。
/// </summary>
public sealed class OneDriveGraphClientFactory
{
    private readonly ILogger<OneDriveGraphClientFactory> _logger;

    public OneDriveGraphClientFactory(ILogger<OneDriveGraphClientFactory> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 创建 Graph 服务客户端。
    /// </summary>
    public async Task<GraphServiceClient> CreateClientAsync(OneDriveNodeOptions options, CancellationToken cancellationToken)
    {
        var scopes = GetScopes(options);
        TokenCredential credential = options.AuthMode switch
        {
            "DeviceCode" => CreateDeviceCodeCredential(options),
            "InteractiveBrowser" => await CreateInteractiveBrowserCredentialAsync(options, ensureAuthenticated: false, cancellationToken),
            _ => throw new NotSupportedException($"不支持的认证模式: {options.AuthMode}。支持的认证模式: DeviceCode, InteractiveBrowser")
        };

        var client = new GraphServiceClient(credential, scopes);
        _logger.LogInformation("已创建 OneDrive Graph 客户端，认证模式: {AuthMode}", options.AuthMode);

        return client;
    }

    public async Task EnsureAuthenticationAsync(OneDriveNodeOptions options, CancellationToken cancellationToken)
    {
        if (!string.Equals(options.AuthMode, "InteractiveBrowser", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _ = await CreateInteractiveBrowserCredentialAsync(options, ensureAuthenticated: true, cancellationToken);
    }

    /// <summary>
    /// WarmAuth 预检查：只允许使用已持久化认证记录。
    /// 若认证记录缺失或损坏，返回带 reauth-required 语义的受控失败。
    /// </summary>
    public async Task EnsureWarmAuthenticationReadyAsync(OneDriveNodeOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!string.Equals(options.AuthMode, "InteractiveBrowser", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(options.ClientId))
        {
            throw CreateReauthRequiredException("缺少 ClientId，无法定位 OneDrive 认证记录。", options.ClientId);
        }

        if (!OneDriveAuthenticationRecordStore.Exists(options.ClientId))
        {
            throw CreateReauthRequiredException("未找到持久化 OneDrive 认证记录。", options.ClientId);
        }

        var authenticationRecord = await OneDriveAuthenticationRecordStore.LoadAsync(options.ClientId, cancellationToken);
        if (authenticationRecord is null)
        {
            throw CreateReauthRequiredException("OneDrive 认证记录已损坏或不可读取。", options.ClientId);
        }
    }

    private DeviceCodeCredential CreateDeviceCodeCredential(OneDriveNodeOptions options)
    {
        // 对于仅 Microsoft 个人账户的应用，需要将 tenant 从 "common" 改为 "consumers"
        // 参考：https://learn.microsoft.com/azure/active-directory/develop/v2-protocols-device-code
        var tenantId = options.TenantId;
        if (tenantId.Equals("common", StringComparison.OrdinalIgnoreCase))
        {
            // 尝试使用 consumers 端点（个人账户专用）
            tenantId = "consumers";
            _logger.LogDebug("DeviceCode 认证使用 consumers 端点（适合 Microsoft 个人账户）");
        }

        _logger.LogInformation("使用 DeviceCode 认证，租户: {TenantId}", tenantId);

        var optionsCred = new DeviceCodeCredentialOptions
        {
            TenantId = tenantId,
            ClientId = options.ClientId,
            DeviceCodeCallback = (context, cancellationToken) =>
            {
                _logger.LogInformation("设备代码认证: {Message}", context.Message);
                // 在控制台显示设备代码，便于用户复制
                Console.WriteLine();
                Console.WriteLine("========================================");
                Console.WriteLine("OneDrive 设备代码授权");
                Console.WriteLine("========================================");
                Console.WriteLine(context.Message);
                Console.WriteLine("========================================");
                Console.WriteLine();
                return Task.CompletedTask;
            },
            TokenCachePersistenceOptions = new TokenCachePersistenceOptions
            {
                Name = $"onedrive_{options.ClientId}",
                UnsafeAllowUnencryptedStorage = false
            }
        };

        return new DeviceCodeCredential(optionsCred);
    }

    /// <summary>
    /// 创建交互式浏览器认证凭证。
    /// 使用 OAuth 2.0 Authorization Code Flow with PKCE，适合桌面应用。
    /// 自动打开系统浏览器，用户授权后自动回调到本地端口。
    /// </summary>
    private async Task<InteractiveBrowserCredential> CreateInteractiveBrowserCredentialAsync(OneDriveNodeOptions options, bool ensureAuthenticated, CancellationToken cancellationToken)
    {
        // 对于 Microsoft 个人账户（OneDrive 个人版），必须使用 consumers 端点
        // 使用 common 端点会导致 userAudience 配置错误
        var tenantId = options.TenantId;
        if (tenantId.Equals("common", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(tenantId))
        {
            tenantId = "consumers";
            _logger.LogDebug("InteractiveBrowser 认证使用 consumers 端点（适合 Microsoft 个人账户）");
        }

        _logger.LogInformation("使用 InteractiveBrowser 认证，租户: {TenantId}，回调端口: {RedirectPort}",
            tenantId, options.InteractiveBrowserRedirectPort);

        var callbackUri = new Uri($"http://localhost:{options.InteractiveBrowserRedirectPort}");

        var tokenCachePersistenceOptions = new TokenCachePersistenceOptions
        {
            Name = $"onedrive_interactive_{options.ClientId}",
            UnsafeAllowUnencryptedStorage = false
        };

        var authenticationRecord = await OneDriveAuthenticationRecordStore.LoadAsync(options.ClientId, cancellationToken);

        var credentialOptions = new InteractiveBrowserCredentialOptions
        {
            TenantId = tenantId,
            ClientId = options.ClientId,
            RedirectUri = callbackUri,
            TokenCachePersistenceOptions = tokenCachePersistenceOptions,
            AuthenticationRecord = authenticationRecord
        };

        var credential = new InteractiveBrowserCredential(credentialOptions);
        if (ensureAuthenticated && authenticationRecord is null)
        {
            _logger.LogInformation("未找到 OneDrive 认证记录，正在执行一次交互式认证并持久化。");
            var newAuthenticationRecord = await credential.AuthenticateAsync(new TokenRequestContext(GetScopes(options)), cancellationToken);
            await OneDriveAuthenticationRecordStore.SaveAsync(options.ClientId, newAuthenticationRecord, cancellationToken);

            credential = new InteractiveBrowserCredential(new InteractiveBrowserCredentialOptions
            {
                TenantId = tenantId,
                ClientId = options.ClientId,
                RedirectUri = callbackUri,
                TokenCachePersistenceOptions = tokenCachePersistenceOptions,
                AuthenticationRecord = newAuthenticationRecord
            });
        }

        return credential;
    }

    private static string[] GetScopes(OneDriveNodeOptions options)
    {
        // 【显式 Graph scopes】InteractiveBrowserCredential 的无参 AuthenticateAsync 会回退到 Azure 管理资源，
        // 这里统一显式传入 OneDrive/Graph 委托权限，避免出现 management.azure.com 的 invalid_scope。
        var scopes = options.Scopes
            .Split([' ', '\t', '\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return scopes.Length > 0
            ? scopes
            : ["Files.ReadWrite", "offline_access", "User.Read"];
    }

    private static InvalidOperationException CreateReauthRequiredException(string reason, string? clientId)
    {
        var recordPath = OneDriveAuthenticationRecordStore.GetRecordPath(clientId ?? string.Empty);
        return new InvalidOperationException(
            $"OneDrive WarmAuth 前置检查失败（reauth-required）：{reason} 请重新执行 ColdAuth 交互授权生成认证记录。AuthRecordPath={recordPath}");
    }
}

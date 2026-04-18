namespace UniversalSyncService.Core.Nodes.OneDrive;

/// <summary>
/// OneDrive 节点配置选项。
/// </summary>
public sealed class OneDriveNodeOptions
{
    private static readonly char[] InvalidPathCharacters = ['"', '*', ':', '<', '>', '?', '\\', '|'];

    /// <summary>
    /// Azure AD 租户 ID。
    /// 对于个人版 OneDrive，使用 "common"。
    /// </summary>
    public string TenantId { get; set; } = "common";

    /// <summary>
    /// Azure AD 应用程序客户端 ID。
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// 认证模式。
    /// 支持：InteractiveBrowser（交互式浏览器，推荐）、DeviceCode（设备代码流）。
    /// </summary>
    public string AuthMode { get; set; } = "InteractiveBrowser";

    /// <summary>
    /// 目标用户 ID 或 UPN。
    /// 对于个人版使用 "me"，企业版可指定具体用户。
    /// </summary>
    public string UserId { get; set; } = "me";

    /// <summary>
    /// 驱动器选择器。
    /// "Me" = 用户默认驱动器，"DriveId" = 指定驱动器 ID。
    /// </summary>
    public string DriveSelector { get; set; } = "Me";

    /// <summary>
    /// 指定驱动器 ID（当 DriveSelector 为 "DriveId" 时使用）。
    /// </summary>
    public string? DriveId { get; set; }

    /// <summary>
    /// 同步根路径（OneDrive 绝对显示路径）。
    /// 例如："/Documents/UniversalSync"，根目录使用 "/"。
    /// </summary>
    public string RootPath { get; set; } = "/";

    /// <summary>
    /// 令牌缓存路径（相对于应用程序目录）。
    /// </summary>
    public string TokenCachePath { get; set; } = "data/auth/onedrive";

    /// <summary>
    /// OAuth 权限范围。
    /// 默认：Files.ReadWrite offline_access User.Read
    /// </summary>
    public string Scopes { get; set; } = "Files.ReadWrite offline_access User.Read";

    /// <summary>
    /// 大文件上传阈值（字节）。
    /// 超过此大小的文件使用分块上传。默认 4 MB。
    /// </summary>
    public long LargeFileThresholdBytes { get; set; } = 4 * 1024 * 1024;

    /// <summary>
    /// 上传分块大小（字节）。
    /// 必须是 320 KB 的倍数。默认 5 MB。
    /// </summary>
    public int UploadSliceSizeBytes { get; set; } = 5 * 1024 * 1024;

    /// <summary>
    /// 冲突处理行为。
    /// replace = 替换，rename = 重命名，fail = 失败。
    /// </summary>
    public string ConflictBehavior { get; set; } = "replace";

    /// <summary>
    /// 连接超时时间（毫秒）。
    /// </summary>
    public int ConnectTimeoutMs { get; set; } = 30000;

    /// <summary>
    /// 最大重试次数。
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// 基础退避延迟（毫秒）。
    /// </summary>
    public int BackoffMs { get; set; } = 1000;

    /// <summary>
    /// InteractiveBrowser 认证模式下的本地回调端口。
    /// 默认使用 8765，Azure AD 应用程序需要配置此重定向 URI。
    /// </summary>
    public int InteractiveBrowserRedirectPort { get; set; } = 8765;

    /// <summary>
    /// 从 NodeConfiguration.ConnectionSettings 解析配置。
    /// 配置优先级：ConnectionSettings > 环境变量 > 默认值
    /// </summary>
    public static OneDriveNodeOptions FromConnectionSettings(IDictionary<string, string> settings)
    {
        var options = new OneDriveNodeOptions();

        options.ClientId = Environment.GetEnvironmentVariable("ONEDRIVE_CLIENT_ID") ?? options.ClientId;
        options.TenantId = Environment.GetEnvironmentVariable("ONEDRIVE_TENANT_ID") ?? options.TenantId;

        if (settings.TryGetValue("TenantId", out var tenantId))
            options.TenantId = tenantId;

        if (settings.TryGetValue("ClientId", out var clientId))
            options.ClientId = clientId;

        if (settings.TryGetValue("AuthMode", out var authMode))
            options.AuthMode = authMode;

        if (settings.TryGetValue("UserId", out var userId))
            options.UserId = userId;

        if (settings.TryGetValue("DriveSelector", out var driveSelector))
            options.DriveSelector = driveSelector;

        if (settings.TryGetValue("DriveId", out var driveId))
            options.DriveId = driveId;

        if (settings.TryGetValue("RootPath", out var rootPath))
            options.RootPath = rootPath;

        if (settings.TryGetValue("TokenCachePath", out var tokenCachePath))
            options.TokenCachePath = tokenCachePath;

        if (settings.TryGetValue("Scopes", out var scopes))
            options.Scopes = scopes;

        if (settings.TryGetValue("LargeFileThresholdBytes", out var largeFileThreshold))
            if (long.TryParse(largeFileThreshold, out var lftValue))
                options.LargeFileThresholdBytes = lftValue;

        if (settings.TryGetValue("UploadSliceSizeBytes", out var uploadSliceSize))
            if (int.TryParse(uploadSliceSize, out var ussValue))
                options.UploadSliceSizeBytes = ussValue;

        if (settings.TryGetValue("ConflictBehavior", out var conflictBehavior))
            options.ConflictBehavior = conflictBehavior;

        if (settings.TryGetValue("ConnectTimeoutMs", out var connectTimeout))
            if (int.TryParse(connectTimeout, out var ctValue))
                options.ConnectTimeoutMs = ctValue;

        if (settings.TryGetValue("MaxRetries", out var maxRetries))
            if (int.TryParse(maxRetries, out var mrValue))
                options.MaxRetries = mrValue;

        if (settings.TryGetValue("BackoffMs", out var backoffMs))
            if (int.TryParse(backoffMs, out var bmValue))
                options.BackoffMs = bmValue;

        if (settings.TryGetValue("InteractiveBrowserRedirectPort", out var redirectPort))
            if (int.TryParse(redirectPort, out var rpValue))
                options.InteractiveBrowserRedirectPort = rpValue;

        return options;
    }

    /// <summary>
    /// 验证配置是否有效。
    /// </summary>
    public bool Validate(out string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(ClientId))
        {
            errorMessage = "ClientId 不能为空";
            return false;
        }

        if (string.IsNullOrWhiteSpace(RootPath))
        {
            errorMessage = "OneDrive RootPath 不能为空，请填写 / 或 /Documents/UniversalSync 这类以 / 开头的远端绝对路径。";
            return false;
        }

        if (IsWindowsAbsolutePath(RootPath) || RootPath.StartsWith("\\\\", StringComparison.Ordinal))
        {
            errorMessage = "OneDrive RootPath 必须是以 / 开头的远端绝对路径，不能使用本机绝对路径。";
            return false;
        }

        var trimmedRootPath = RootPath.Trim();
        if (!trimmedRootPath.StartsWith("/", StringComparison.Ordinal))
        {
            errorMessage = "OneDrive RootPath 必须以 / 开头，例如 / 或 /Documents/UniversalSync。";
            return false;
        }

        var normalizedRootPath = NormalizeRemotePath(trimmedRootPath);
        var invalidSegment = normalizedRootPath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(segment => segment.IndexOfAny(InvalidPathCharacters) >= 0);
        if (invalidSegment is not null)
        {
            errorMessage = $"OneDrive RootPath 包含非法目录名片段“{invalidSegment}”。请使用以 / 开头的远端绝对路径，且不要包含 : * ? < > \\ | 等字符。";
            return false;
        }

        RootPath = string.IsNullOrWhiteSpace(normalizedRootPath) ? "/" : $"/{normalizedRootPath}";

        if (DriveSelector == "DriveId" && string.IsNullOrWhiteSpace(DriveId))
        {
            errorMessage = "DriveSelector 为 DriveId 时 DriveId 不能为空";
            return false;
        }

        errorMessage = null;
        return true;
    }

    private static string NormalizeRemotePath(string path)
    {
        return path.Replace('\\', '/').Trim('/');
    }

    private static bool IsWindowsAbsolutePath(string path)
    {
        var trimmed = path.Trim();
        return trimmed.StartsWith("\\\\", StringComparison.Ordinal)
            || (trimmed.Length >= 3
                && char.IsLetter(trimmed[0])
                && trimmed[1] == ':'
                && (trimmed[2] == '\\' || trimmed[2] == '/'));
    }
}

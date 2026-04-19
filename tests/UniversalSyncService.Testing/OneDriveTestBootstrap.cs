using UniversalSyncService.Core.Nodes.OneDrive;
using System.Text.RegularExpressions;

namespace UniversalSyncService.Testing;

/// <summary>
/// OneDrive 测试引导：本地覆盖与持久化凭据探测。
/// </summary>
public static class OneDriveTestBootstrap
{
    private const string PlaceholderClientId = "PLACEHOLDER_CLIENT_ID";

    public sealed record OneDriveLaneConfiguration(
        string? ClientId,
        string TenantId,
        bool EnableOnlineColdAuth,
        string TemplatePath,
        string LocalOverridePath,
        bool LocalOverrideExists,
        string? CredentialSource);

    public static string GetTemplatePath(string templateFileName)
    {
        return TestConfigPaths.GetTemplatePath(templateFileName);
    }

    public static string GetLocalOverridePath(string templateFileName)
    {
        return TestConfigPaths.GetLocalOverridePath(templateFileName);
    }

    public static OneDriveLaneConfiguration ResolveLaneConfiguration(string templateFileName)
    {
        var templatePath = GetTemplatePath(templateFileName);
        var localOverridePath = GetLocalOverridePath(templateFileName);

        var templateValues = ReadOneDriveConfigValues(templatePath);
        var localOverrideExists = File.Exists(localOverridePath);
        var localValues = localOverrideExists
            ? ReadOneDriveConfigValues(localOverridePath)
            : (ClientId: (string?)null, TenantId: (string?)null, EnableOnlineColdAuth: (bool?)null);

        var resolvedClientId = NormalizeClientId(templateValues.ClientId);
        var resolvedTenantId = NormalizeTenantId(templateValues.TenantId) ?? "common";
        var enableOnlineColdAuth = templateValues.EnableOnlineColdAuth ?? false;
        var credentialSource = "template";

        if (!string.IsNullOrWhiteSpace(localValues.ClientId))
        {
            resolvedClientId = NormalizeClientId(localValues.ClientId);
            credentialSource = "local-override";
        }

        if (!string.IsNullOrWhiteSpace(localValues.TenantId))
        {
            resolvedTenantId = NormalizeTenantId(localValues.TenantId) ?? resolvedTenantId;
        }

        if (localValues.EnableOnlineColdAuth.HasValue)
        {
            enableOnlineColdAuth = localValues.EnableOnlineColdAuth.Value;
        }

        var persistedCredentials = OneDriveAppCredentials.LoadCredentials();
        if (persistedCredentials is not null)
        {
            resolvedClientId = NormalizeClientId(persistedCredentials.ClientId);
            resolvedTenantId = NormalizeTenantId(persistedCredentials.TenantId) ?? resolvedTenantId;
            credentialSource = "persisted-credentials";
        }

        return new OneDriveLaneConfiguration(
            resolvedClientId,
            resolvedTenantId,
            enableOnlineColdAuth,
            templatePath,
            localOverridePath,
            localOverrideExists,
            credentialSource);
    }

    public static async Task EnsureWarmAuthRecordReadyAsync(string clientId, CancellationToken cancellationToken = default)
    {
        if (OneDriveAuthenticationRecordStore.Exists(clientId))
        {
            return;
        }

        await Task.Yield();
        throw new InvalidOperationException(
            $"未找到 OneDrive 持久化认证记录（reauth-required）：{OneDriveAuthenticationRecordStore.GetRecordPath(clientId)}");
    }

    private static (string? ClientId, string? TenantId, bool? EnableOnlineColdAuth) ReadOneDriveConfigValues(string configPath)
    {
        if (!File.Exists(configPath))
        {
            return (null, null, null);
        }

        var content = File.ReadAllText(configPath);
        var clientId = ReadYamlScalar(content, "ClientId");
        var tenantId = ReadYamlScalar(content, "TenantId");
        var enableOnlineColdAuth = ReadYamlBoolean(content, "EnableOnlineColdAuth");

        return (clientId, tenantId, enableOnlineColdAuth);
    }

    private static string? ReadYamlScalar(string yamlContent, string key)
    {
        var match = Regex.Match(yamlContent, $"{Regex.Escape(key)}\\s*:\\s*\"?(?<value>[^\"\\r\\n#]+)\"?", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["value"].Value.Trim() : null;
    }

    private static bool? ReadYamlBoolean(string yamlContent, string key)
    {
        var rawValue = ReadYamlScalar(yamlContent, key);
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        return rawValue.Equals("true", StringComparison.OrdinalIgnoreCase)
            || rawValue.Equals("1", StringComparison.OrdinalIgnoreCase)
            || rawValue.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeClientId(string? rawClientId)
    {
        if (string.IsNullOrWhiteSpace(rawClientId))
        {
            return null;
        }

        var normalized = rawClientId.Trim();
        return normalized.Equals(PlaceholderClientId, StringComparison.OrdinalIgnoreCase)
            ? null
            : normalized;
    }

    private static string? NormalizeTenantId(string? rawTenantId)
    {
        return string.IsNullOrWhiteSpace(rawTenantId)
            ? null
            : rawTenantId.Trim();
    }
}

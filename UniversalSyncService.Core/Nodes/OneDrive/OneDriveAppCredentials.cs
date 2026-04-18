using System.Text;
using System.Text.Json;

namespace UniversalSyncService.Core.Nodes.OneDrive;

/// <summary>
/// OneDrive 应用程序凭据存储。
/// 仅供开发者配置 Azure AD 应用程序凭据（ClientId）。
/// 优先写入 Host 项目下的 data/secrets，便于随宿主一起构建和发布。
/// </summary>
public static class OneDriveAppCredentials
{
    private static readonly string RuntimeCredentialDirectory = Path.Combine(
        AppContext.BaseDirectory,
        "data",
        "secrets");

    private static readonly string RuntimeCredentialFilePath = Path.Combine(
        RuntimeCredentialDirectory,
        "onedrive-app-credentials.dat");

    private static readonly string? HostProjectCredentialFilePath = ResolveHostProjectCredentialFilePath();

    private static readonly string LocalUserCredentialFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "UniversalSyncService",
        "data",
        "secrets",
        "onedrive-app-credentials.dat");

    private static readonly string CommonApplicationCredentialFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "UniversalSyncService",
        "data",
        "secrets",
        "onedrive-app-credentials.dat");

    /// <summary>
    /// 保存应用程序凭据（开发者使用）。
    /// </summary>
    public static void SaveCredentials(string clientId, string? tenantId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

        Directory.CreateDirectory(GetPrimaryCredentialDirectory());

        var credentials = new AppCredentialData
        {
            ClientId = clientId,
            TenantId = tenantId ?? "common",
            ConfiguredAt = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(credentials, new JsonSerializerOptions
        {
            WriteIndented = false
        });
        var data = Encoding.UTF8.GetBytes(json);

        // Windows 平台使用 DPAPI 加密。
        if (OperatingSystem.IsWindows())
        {
            data = System.Security.Cryptography.ProtectedData.Protect(
                data,
                null,
                System.Security.Cryptography.DataProtectionScope.LocalMachine);
        }

        File.WriteAllBytes(GetPrimaryCredentialFilePath(), data);

        // 【存储收敛】凭据主副本以当前首选路径为准，旧位置仅保留读取兼容，
        // 保存成功后主动清理，避免宿主/配置工具各自读到不同副本。
        DeleteLegacyCredentialCopies();
    }

    /// <summary>
    /// 加载应用程序凭据。
    /// </summary>
    public static AppCredentialData? LoadCredentials()
    {
        var credentialPath = ResolveExistingCredentialPath();
        if (credentialPath is null)
        {
            return null;
        }

        var data = File.ReadAllBytes(credentialPath);
        var persistedBytes = data;

        if (OperatingSystem.IsWindows() && data.Length > 0)
        {
            try
            {
                data = System.Security.Cryptography.ProtectedData.Unprotect(
                    data,
                    null,
                    System.Security.Cryptography.DataProtectionScope.LocalMachine);
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                return null;
            }
        }

        var json = Encoding.UTF8.GetString(data);
        var credentials = JsonSerializer.Deserialize<AppCredentialData>(json);
        if (credentials is null || string.IsNullOrWhiteSpace(credentials.ClientId))
        {
            return null;
        }

        PromoteLegacyCredentialCopyIfNeeded(credentialPath, persistedBytes);
        return credentials;
    }

    public static bool IsConfigured()
    {
        return ResolveExistingCredentialPath() is not null;
    }

    public static void DeleteCredentials()
    {
        foreach (var path in GetCandidateCredentialPaths().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    public static string GetStoragePath()
    {
        return GetPrimaryCredentialFilePath();
    }

    private static string? ResolveExistingCredentialPath()
    {
        foreach (var path in GetCandidateCredentialPaths().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static IEnumerable<string> GetCandidateCredentialPaths()
    {
        yield return GetPrimaryCredentialFilePath();

        if (!string.IsNullOrWhiteSpace(HostProjectCredentialFilePath))
        {
            yield return HostProjectCredentialFilePath;
        }

        yield return RuntimeCredentialFilePath;
        yield return LocalUserCredentialFilePath;
        yield return CommonApplicationCredentialFilePath;
    }

    private static void PromoteLegacyCredentialCopyIfNeeded(string loadedPath, byte[] persistedBytes)
    {
        if (string.Equals(loadedPath, GetPrimaryCredentialFilePath(), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(GetPrimaryCredentialDirectory());
            File.WriteAllBytes(GetPrimaryCredentialFilePath(), persistedBytes);
            DeleteLegacyCredentialCopies();
        }
        catch
        {
            // 【迁移容错】旧位置提升失败时，至少保持当前读取流程可用。
        }
    }

    private static void DeleteLegacyCredentialCopies()
    {
        DeleteFileIfExists(RuntimeCredentialFilePath);
        DeleteFileIfExists(HostProjectCredentialFilePath);
        DeleteFileIfExists(LocalUserCredentialFilePath);
        DeleteFileIfExists(CommonApplicationCredentialFilePath);
    }

    private static void DeleteFileIfExists(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || string.Equals(path, GetPrimaryCredentialFilePath(), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // 【清理容错】旧副本清理失败不应阻断主流程。
        }
    }

    private static string GetPrimaryCredentialFilePath()
    {
        return HostProjectCredentialFilePath ?? RuntimeCredentialFilePath;
    }

    private static string GetPrimaryCredentialDirectory()
    {
        return Path.GetDirectoryName(GetPrimaryCredentialFilePath()) ?? RuntimeCredentialDirectory;
    }

    private static string? ResolveHostProjectCredentialFilePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var hostProjectPath = Path.Combine(directory.FullName, "UniversalSyncService.Host", "UniversalSyncService.Host.csproj");
            if (File.Exists(hostProjectPath))
            {
                return Path.Combine(directory.FullName, "UniversalSyncService.Host", "data", "secrets", "onedrive-app-credentials.dat");
            }

            directory = directory.Parent;
        }

        return null;
    }
}

/// <summary>
/// 应用程序凭据数据结构。
/// </summary>
public sealed class AppCredentialData
{
    public string ClientId { get; set; } = string.Empty;

    public string TenantId { get; set; } = "common";

    public DateTime ConfiguredAt { get; set; }
}

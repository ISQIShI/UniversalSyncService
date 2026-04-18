using Azure.Identity;

namespace UniversalSyncService.Core.Nodes.OneDrive;

/// <summary>
/// OneDrive 交互式认证记录持久化存储。
/// 认证记录不包含访问令牌本体，用于在持久化 token cache 中定位用户账户，实现静默续期。
/// </summary>
public static class OneDriveAuthenticationRecordStore
{
    private static readonly string StorageDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "UniversalSyncService",
        "data",
        "auth",
        "onedrive-records");

    public static async Task<AuthenticationRecord?> LoadAsync(string clientId, CancellationToken cancellationToken)
    {
        var recordPath = GetRecordPath(clientId);
        if (!File.Exists(recordPath))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(recordPath);
            return await AuthenticationRecord.DeserializeAsync(stream, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public static async Task SaveAsync(string clientId, AuthenticationRecord authenticationRecord, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(StorageDirectory);
        var recordPath = GetRecordPath(clientId);
        await using var stream = File.Create(recordPath);
        await authenticationRecord.SerializeAsync(stream, cancellationToken);
    }

    private static string GetRecordPath(string clientId)
    {
        var normalizedClientId = string.IsNullOrWhiteSpace(clientId)
            ? "unknown-client"
            : string.Join('_', clientId.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        return Path.Combine(StorageDirectory, $"{normalizedClientId}.authrecord");
    }
}

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

    /// <summary>
    /// 检查指定客户端是否存在持久化认证记录。
    /// </summary>
    public static bool Exists(string clientId)
    {
        var recordPath = GetRecordPath(clientId);
        return File.Exists(recordPath);
    }

    /// <summary>
    /// 删除指定客户端的认证记录（若存在）。
    /// </summary>
    public static Task DeleteIfExistsAsync(string clientId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var recordPath = GetRecordPath(clientId);
        if (File.Exists(recordPath))
        {
            File.Delete(recordPath);
        }

        return Task.CompletedTask;
    }

    public static string GetRecordPath(string clientId)
    {
        var normalizedClientId = string.IsNullOrWhiteSpace(clientId)
            ? "unknown-client"
            : string.Join('_', clientId.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        return Path.Combine(StorageDirectory, $"{normalizedClientId}.authrecord");
    }
}

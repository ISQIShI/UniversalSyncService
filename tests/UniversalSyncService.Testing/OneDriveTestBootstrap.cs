using UniversalSyncService.Core.Nodes.OneDrive;

namespace UniversalSyncService.Testing;

/// <summary>
/// OneDrive 测试引导：本地覆盖与持久化凭据探测。
/// </summary>
public static class OneDriveTestBootstrap
{
    public static string GetTemplatePath(string templateFileName)
    {
        return TestConfigPaths.GetTemplatePath(templateFileName);
    }

    public static string GetLocalOverridePath(string templateFileName)
    {
        return TestConfigPaths.GetLocalOverridePath(templateFileName);
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
}

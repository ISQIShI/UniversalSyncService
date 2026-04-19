using UniversalSyncService.Core.Nodes.OneDrive;

namespace UniversalSyncService.Tools;

/// <summary>
/// OneDrive 应用程序凭据配置工具。
/// 供开发者配置 Azure AD 应用程序凭据（ClientId）。
/// </summary>
class OneDriveCredentialConfigurator
{
    static void Main(string[] args)
    {
        Console.WriteLine("=== Universal Sync Service - OneDrive 凭据配置工具 ===");
        Console.WriteLine();

        // 检查当前配置
        if (OneDriveAppCredentials.IsConfigured())
        {
            var existing = OneDriveAppCredentials.LoadCredentials();
            Console.WriteLine("当前已配置凭据：");
            Console.WriteLine($"  ClientId: {existing?.ClientId[..Math.Min(8, existing?.ClientId?.Length ?? 0)]}...");
            Console.WriteLine($"  TenantId: {existing?.TenantId}");
            Console.WriteLine($"  配置时间: {existing?.ConfiguredAt:yyyy-MM-dd HH:mm:ss} UTC");
            Console.WriteLine();
            Console.Write("是否重新配置？(y/N): ");
            var response = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (response != "y" && response != "yes")
            {
                Console.WriteLine("配置未更改。");
                return;
            }
            Console.WriteLine();
        }

        // 指导信息
        Console.WriteLine("请提供您的 Azure AD 应用程序凭据。");
        Console.WriteLine("如果还没有注册应用程序，请访问:");
        Console.WriteLine("  https://portal.azure.com/#blade/Microsoft_AAD_RegisteredApps/ApplicationsListBlade");
        Console.WriteLine();
        Console.WriteLine("注册步骤：");
        Console.WriteLine("  1. 点击 'New registration'");
        Console.WriteLine("  2. 输入应用名称");
        Console.WriteLine("  3. 选择 'Accounts in any organizational directory and personal Microsoft accounts'");
        Console.WriteLine("  4. 在 'API Permissions' 中添加 'Microsoft Graph' > 'Delegated permissions' > 'Files.ReadWrite'");
        Console.WriteLine("  5. 在 'Overview' 页面复制 'Application (client) ID'");
        Console.WriteLine();

        // 输入 ClientId
        string? clientId = null;
        while (string.IsNullOrWhiteSpace(clientId))
        {
            Console.Write("请输入 Client ID: ");
            clientId = Console.ReadLine()?.Trim();

            if (string.IsNullOrWhiteSpace(clientId))
            {
                Console.WriteLine("Client ID 不能为空，请重新输入。");
            }
            else if (clientId.Length < 30)
            {
                Console.WriteLine("Client ID 格式不正确（应为 GUID 格式，约 36 个字符），请检查。");
                clientId = null;
            }
        }

        // 输入 TenantId
        Console.Write("请输入 Tenant ID [默认: common]: ");
        var tenantId = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            tenantId = "common";
        }

        // 保存凭据
        try
        {
            OneDriveAppCredentials.SaveCredentials(clientId, tenantId);

            Console.WriteLine();
            Console.WriteLine("✓ 凭据已成功保存！");
            Console.WriteLine($"存储位置: {OneDriveAppCredentials.GetStoragePath()}");
            Console.WriteLine("旧的用户级/运行目录凭据副本已在保存后尝试自动清理。\n开发期会优先写入 Host 项目下的 data/secrets，便于后续构建/发布时随宿主一起输出；脱离源码仓库运行时则回退到当前运行目录。\n如存在之前生成的旧位置凭据文件，请以当前输出路径为准。");
            Console.WriteLine();
            Console.WriteLine("凭据将在应用程序启动时自动加载。");
            Console.WriteLine("用户可以在配置文件中设置自己的 OneDrive 账号信息。");
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine($"✗ 保存凭据时出错: {ex.Message}");
            Environment.ExitCode = 1;
        }
    }
}

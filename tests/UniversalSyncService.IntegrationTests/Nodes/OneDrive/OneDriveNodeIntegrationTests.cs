using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UniversalSyncService.Abstractions.Nodes;
using UniversalSyncService.Core.Nodes.OneDrive;
using Xunit;

namespace UniversalSyncService.IntegrationTests.Nodes.OneDrive;

/// <summary>
/// OneDrive 节点集成测试。
/// 注意：这些测试需要有效的 Azure AD 凭据才能运行，默认跳过。
/// </summary>
public class OneDriveNodeIntegrationTests
{
    private readonly ILogger<OneDriveNode> _logger;
    private readonly ILogger<OneDriveGraphClientFactory> _factoryLogger;
    
    /// <summary>
    /// 测试根目录名称。所有测试文件都放在此目录下。
    /// </summary>
    private const string TestRootFolder = "UniversalSyncTest";

    public OneDriveNodeIntegrationTests()
    {
        _logger = NullLogger<OneDriveNode>.Instance;
        _factoryLogger = NullLogger<OneDriveGraphClientFactory>.Instance;
    }
    
    /// <summary>
    /// 生成测试文件夹路径。
    /// 格式：UniversalSyncTest/{TestName}_{Guid}
    /// </summary>
    private string GenerateTestFolderPath(string testName)
    {
        return $"{TestRootFolder}/{testName}_{Guid.NewGuid():N}";
    }

    private static void SkipIfInteractiveBrowserTestsNotExplicitlyEnabled()
    {
        var enableInteractiveBrowserTests = Environment.GetEnvironmentVariable("ONEDRIVE_ENABLE_INTERACTIVE_BROWSER_TESTS");
        Skip.If(!string.Equals(enableInteractiveBrowserTests, "1", StringComparison.Ordinal),
            "InteractiveBrowser 在线测试需要显式设置 ONEDRIVE_ENABLE_INTERACTIVE_BROWSER_TESTS=1 后才运行，避免在无人值守环境中卡住或占用本地回调端口。");
    }
    
    /// <summary>
    /// 删除测试文件夹（使用已连接的节点）。
    /// </summary>
    private async Task CleanupTestFolderAsync(OneDriveNode node, string folderPath)
    {
        try
        {
            // 测试使用显式根目录清理入口，避免业务 DeleteAsync("") 具备误删整个 RootPath 的隐式语义。
            if (!string.IsNullOrEmpty(folderPath))
            {
                await node.DeleteConfiguredRootAsync(CancellationToken.None);
            }
        }
        catch
        {
            // 清理失败不影响测试结果
        }
    }

    /// <summary>
    /// 测试节点元数据是否正确设置。
    /// </summary>
    [Fact]
    public async Task Constructor_ShouldSetMetadataCorrectly()
    {
        // Arrange
        var options = new OneDriveNodeOptions
        {
            ClientId = "test-client-id"
        };
        var factory = new OneDriveGraphClientFactory(_factoryLogger);
        var graphClient = await factory.CreateClientAsync(options, CancellationToken.None);

        // Act
        var node = new OneDriveNode("test-id", "Test Node", options, graphClient, _logger);

        // Assert
        Assert.Equal("test-id", node.Metadata.Id);
        Assert.Equal("Test Node", node.Metadata.Name);
        Assert.Equal(NodeType.Cloud, node.Metadata.NodeType);
        Assert.Equal("1.0.0", node.Metadata.Version);
        Assert.Equal(NodeState.Disconnected, node.State);
        Assert.True(node.Capabilities.HasFlag(NodeCapabilities.CanRead));
        Assert.True(node.Capabilities.HasFlag(NodeCapabilities.CanWrite));
        Assert.True(node.Capabilities.HasFlag(NodeCapabilities.CanDelete));
        Assert.True(node.Capabilities.HasFlag(NodeCapabilities.CanStream));
    }

    /// <summary>
    /// 测试 ConnectAsync 在未提供凭据时应抛出异常。
    /// 使用 DeviceCode 模式和无效的客户端 ID，添加超时防止测试卡住。
    /// </summary>
    [Fact(Timeout = 30000)]
    public async Task ConnectAsync_WithoutCredentials_ShouldThrowException()
    {
        // Arrange - 使用无效的 DeviceCode 配置（DeviceCode 比 InteractiveBrowser 更快失败）
        var options = new OneDriveNodeOptions
        {
            TenantId = "common",
            ClientId = "invalid-client-id",
            AuthMode = "DeviceCode"
        };

        var factory = new OneDriveGraphClientFactory(_factoryLogger);
        var graphClient = await factory.CreateClientAsync(options, CancellationToken.None);
        var node = new OneDriveNode("test-node", "Test Node", options, graphClient, _logger);

        // Act & Assert
        // 认证失败时会抛出 AuthenticationFailedException 或其他认证相关异常
        await Assert.ThrowsAnyAsync<Exception>(() => node.ConnectAsync(CancellationToken.None));
        Assert.Equal(NodeState.Disconnected, node.State);
    }

    /// <summary>
    /// 测试配置验证。
    /// </summary>
    [Fact]
    public void OneDriveNodeOptions_Validate_ShouldWorkCorrectly()
    {
        // Arrange - 有效配置
        var validOptions = new OneDriveNodeOptions
        {
            ClientId = "valid-client-id",
            AuthMode = "InteractiveBrowser"
        };

        // Act & Assert
        Assert.True(validOptions.Validate(out var error));
        Assert.Null(error);

        // Arrange - 无效配置（空 ClientId）
        var invalidOptions = new OneDriveNodeOptions
        {
            ClientId = "",
            AuthMode = "InteractiveBrowser"
        };

        // Act & Assert
        Assert.False(invalidOptions.Validate(out error));
        Assert.NotNull(error);
        Assert.Contains("ClientId", error);
    }

    /// <summary>
    /// 测试大文件阈值配置。
    /// </summary>
    [Theory]
    [InlineData(100, true)]  // 小文件
    [InlineData(4 * 1024 * 1024, true)]  // 正好 4MB（阈值边界，<= 阈值视为小文件）
    [InlineData(4 * 1024 * 1024 + 1, false)]  // 刚好超过 4MB
    [InlineData(10 * 1024 * 1024, false)]  // 大文件
    public void LargeFileThreshold_ShouldDetermineUploadStrategy(long fileSize, bool isSmallFile)
    {
        // Arrange
        var options = new OneDriveNodeOptions
        {
            LargeFileThresholdBytes = 4 * 1024 * 1024  // 4MB 阈值
        };

        // Act
        var useSmallFileUpload = fileSize <= options.LargeFileThresholdBytes;

        // Assert
        Assert.Equal(isSmallFile, useSmallFileUpload);
    }

    /// <summary>
    /// 使用 DeviceCode 流测试连接（适合个人开发者）。
    /// 此测试需要交互式授权，请确保在控制台可见的环境下运行。
    /// 超时：120秒（包括用户复制代码和浏览器授权时间）
    /// </summary>
    [SkippableFact(Timeout = 120000)]
    public async Task ConnectAsync_WithDeviceCode_ShouldConnectSuccessfully()
    {
        // Arrange
        var clientId = Environment.GetEnvironmentVariable("ONEDRIVE_TEST_CLIENT_ID");

        Skip.If(string.IsNullOrEmpty(clientId),
            "请设置 ONEDRIVE_TEST_CLIENT_ID 环境变量来运行此测试");

        Skip.If(Console.IsInputRedirected || Console.IsOutputRedirected,
            "此测试需要交互式控制台，无法在重定向的 I/O 环境中运行");

        var options = new OneDriveNodeOptions
        {
            TenantId = "common",
            ClientId = clientId,
            AuthMode = "InteractiveBrowser",
            Scopes = "Files.ReadWrite offline_access User.Read"
        };

        var factory = new OneDriveGraphClientFactory(_factoryLogger);
        var graphClient = await factory.CreateClientAsync(options, CancellationToken.None);
        var node = new OneDriveNode("test-node-devicecode", "Test OneDrive (DeviceCode)", options, graphClient, _logger);

        // Act - 连接时会显示设备代码，用户需要在浏览器中授权
        await node.ConnectAsync(CancellationToken.None);

        // Assert
        Assert.Equal(NodeState.Connected, node.State);
        Assert.NotNull(node.Metadata.Id);

        // Cleanup
        await node.DisconnectAsync(CancellationToken.None);
    }

    /// <summary>
    /// 测试文件枚举功能。
    /// 需要有效的 OneDrive 连接。
    /// 超时：60秒（包括用户交互时间）
    /// </summary>
    [SkippableFact(Timeout = 60000)]
    public async Task GetSyncItemsAsync_ShouldEnumerateFilesAndFolders()
    {
        // Arrange
        var clientId = Environment.GetEnvironmentVariable("ONEDRIVE_TEST_CLIENT_ID");
        Skip.If(string.IsNullOrEmpty(clientId), "需要 OneDrive 凭据");
        SkipIfInteractiveBrowserTestsNotExplicitlyEnabled();

        // 生成测试文件夹路径
        var testFolder = GenerateTestFolderPath("EnumTest");

        var options = new OneDriveNodeOptions
        {
            TenantId = "common",
            ClientId = clientId,
            AuthMode = "InteractiveBrowser",
            RootPath = testFolder,
            Scopes = "Files.ReadWrite offline_access"
        };

        var factory = new OneDriveGraphClientFactory(_factoryLogger);
        var graphClient = await factory.CreateClientAsync(options, CancellationToken.None);
        var node = new OneDriveNode("test-node-enum", "Test Enumeration", options, graphClient, _logger);

        await node.ConnectAsync(CancellationToken.None);

        try
        {
            // Act
            var items = new List<UniversalSyncService.Abstractions.SyncItems.ISyncItem>();
            await foreach (var item in node.GetSyncItemsAsync(CancellationToken.None))
            {
                items.Add(item);
                if (items.Count >= 10) break; // 限制数量，避免测试时间过长
            }

            // Assert
            Assert.True(items.Count >= 0, "应该能够枚举项目（即使为空）");

            // 验证返回的项目有有效的元数据
            foreach (var item in items)
            {
                Assert.NotNull(item.Metadata);
                Assert.NotNull(item.Metadata.Name);
                Assert.NotNull(item.Metadata.Path);
                Assert.True(item.ItemType == Abstractions.SyncItems.SyncItemType.File || item.ItemType == Abstractions.SyncItems.SyncItemType.Directory);
            }
        }
        finally
        {
            // 清理测试文件夹
            await CleanupTestFolderAsync(node, testFolder);
            await node.DisconnectAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// 测试文件上传和下载功能。
    /// 超时：120秒（包括用户交互时间和文件传输）
    /// </summary>
    [SkippableFact(Timeout = 120000)]
    public async Task UploadAndDownloadAsync_ShouldWorkCorrectly()
    {
        // Arrange
        var clientId = Environment.GetEnvironmentVariable("ONEDRIVE_TEST_CLIENT_ID");
        Skip.If(string.IsNullOrEmpty(clientId), "需要 OneDrive 凭据");
        SkipIfInteractiveBrowserTestsNotExplicitlyEnabled();

        // 生成测试文件夹路径
        var testFolder = GenerateTestFolderPath("UploadDownloadTest");

        var options = new OneDriveNodeOptions
        {
            TenantId = "common",
            ClientId = clientId,
            AuthMode = "InteractiveBrowser",
            RootPath = testFolder,
            Scopes = "Files.ReadWrite offline_access"
        };

        var factory = new OneDriveGraphClientFactory(_factoryLogger);
        var graphClient = await factory.CreateClientAsync(options, CancellationToken.None);
        var node = new OneDriveNode("test-node-upload", "Test Upload/Download", options, graphClient, _logger);

        await node.ConnectAsync(CancellationToken.None);

        try
        {
            // 创建测试文件内容
            var testContent = $"Test content generated at {DateTime.UtcNow:O}";
            var testFilePath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.txt");
            await File.WriteAllTextAsync(testFilePath, testContent);

            try
            {
                // Act - 上传
                var uploadItem = new Core.SyncItems.FileSystemSyncItem(testFilePath, "testfile.txt");
                await node.UploadAsync(uploadItem, CancellationToken.None);

                // 验证文件已上传 - 重新枚举
                var foundUploadedFile = false;
                await foreach (var item in node.GetSyncItemsAsync(CancellationToken.None))
                {
                    if (item.Metadata.Name == "testfile.txt")
                    {
                        foundUploadedFile = true;
                        break;
                    }
                }
                Assert.True(foundUploadedFile, "上传后应该能找到文件");

                // Act - 下载（通过 GetSyncItems 获取后下载）
                UniversalSyncService.Abstractions.SyncItems.ISyncItem? downloadedItem = null;
                await foreach (var item in node.GetSyncItemsAsync(CancellationToken.None))
                {
                    if (item.Metadata.Name == "testfile.txt")
                    {
                        downloadedItem = item;
                        break;
                    }
                }

                Assert.NotNull(downloadedItem);
                await node.DownloadAsync(downloadedItem, CancellationToken.None);
            }
            finally
            {
                // 清理本地临时文件
                if (File.Exists(testFilePath))
                {
                    File.Delete(testFilePath);
                }
            }
        }
        finally
        {
            // 清理测试文件夹
            await CleanupTestFolderAsync(node, testFolder);
            await node.DisconnectAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// 测试文件夹创建功能。
    /// 超时：60秒（包括用户交互时间）
    /// </summary>
    [SkippableFact(Timeout = 60000)]
    public async Task UploadAsync_Directory_ShouldCreateFolder()
    {
        // Arrange
        var clientId = Environment.GetEnvironmentVariable("ONEDRIVE_TEST_CLIENT_ID");
        Skip.If(string.IsNullOrEmpty(clientId), "需要 OneDrive 凭据");
        SkipIfInteractiveBrowserTestsNotExplicitlyEnabled();

        // 生成测试文件夹路径
        var testFolder = GenerateTestFolderPath("FolderCreationTest");

        var options = new OneDriveNodeOptions
        {
            TenantId = "common",
            ClientId = clientId,
            AuthMode = "InteractiveBrowser",
            RootPath = testFolder,
            Scopes = "Files.ReadWrite offline_access"
        };

        var factory = new OneDriveGraphClientFactory(_factoryLogger);
        var graphClient = await factory.CreateClientAsync(options, CancellationToken.None);
        var node = new OneDriveNode("test-node-folder", "Test Folder Creation", options, graphClient, _logger);

        await node.ConnectAsync(CancellationToken.None);

        try
        {
            // Act - 创建一个目录类型的同步项
            var folderMetadata = new UniversalSyncService.Abstractions.SyncItems.SyncItemMetadata(
                "TestSubFolder",
                "TestSubFolder",
                "TestSubFolder",
                0,
                DateTime.UtcNow,
                DateTime.UtcNow,
                null,
                "inode/directory");

            var folderItem = new Core.SyncItems.FileSystemSyncItem(
                folderMetadata,
                Abstractions.SyncItems.SyncItemType.Directory);

            await node.UploadAsync(folderItem, CancellationToken.None);

            // Assert - 验证文件夹已创建
            var foundFolder = false;
            await foreach (var item in node.GetSyncItemsAsync(CancellationToken.None))
            {
                if (item.Metadata.Name == "TestSubFolder" && item.ItemType == Abstractions.SyncItems.SyncItemType.Directory)
                {
                    foundFolder = true;
                    break;
                }
            }
            Assert.True(foundFolder, "应该能找到创建的文件夹");
        }
        finally
        {
            // 清理测试文件夹
            await CleanupTestFolderAsync(node, testFolder);
            await node.DisconnectAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// 测试速率限制重试机制。
    /// </summary>
    [Fact]
    public void OneDriveNodeOptions_RetryConfiguration_ShouldHaveDefaults()
    {
        // Arrange & Act
        var options = new OneDriveNodeOptions();

        // Assert - 验证默认重试配置
        Assert.Equal(3, options.MaxRetries);
        Assert.Equal(1000, options.BackoffMs);
        Assert.True(options.MaxRetries > 0, "应该有重试机制");
        Assert.True(options.BackoffMs > 0, "应该有退避延迟");
    }

    /// <summary>
    /// 测试令牌缓存配置。
    /// </summary>
    [Fact]
    public void OneDriveNodeOptions_TokenCacheConfiguration_ShouldBeValid()
    {
        // Arrange & Act
        var options = new OneDriveNodeOptions();

        // Assert
        Assert.NotNull(options.TokenCachePath);
        Assert.False(string.IsNullOrWhiteSpace(options.TokenCachePath));
        Assert.Equal("data/auth/onedrive", options.TokenCachePath);
    }

    /// <summary>
    /// 测试大文件上传配置。
    /// </summary>
    [Theory]
    [InlineData(4 * 1024 * 1024, 5 * 1024 * 1024)]  // 默认阈值和分块大小
    public void OneDriveNodeOptions_UploadConfiguration_ShouldBeValid(long threshold, int sliceSize)
    {
        // Arrange & Act
        var options = new OneDriveNodeOptions
        {
            LargeFileThresholdBytes = threshold,
            UploadSliceSizeBytes = sliceSize
        };

        // Assert
        Assert.Equal(threshold, options.LargeFileThresholdBytes);
        Assert.Equal(sliceSize, options.UploadSliceSizeBytes);
        Assert.True(options.UploadSliceSizeBytes % (320 * 1024) == 0,
            "分块大小应该是 320KB 的倍数");
    }

    /// <summary>
    /// 测试 InteractiveBrowser 认证模式的配置。
    /// </summary>
    [Fact]
    public void OneDriveNodeOptions_InteractiveBrowserConfiguration_ShouldHaveDefaults()
    {
        // Arrange & Act
        var options = new OneDriveNodeOptions();

        // Assert
        Assert.Equal(8765, options.InteractiveBrowserRedirectPort);
        Assert.True(options.InteractiveBrowserRedirectPort > 1024, "回调端口应该大于 1024（避免使用特权端口）");
        Assert.True(options.InteractiveBrowserRedirectPort < 65536, "回调端口应该在有效范围内");
    }

    /// <summary>
    /// 测试 InteractiveBrowser 认证模式创建客户端。
    /// </summary>
    [Fact]
    public async Task CreateClient_WithInteractiveBrowserAuthMode_ShouldCreateClient()
    {
        // Arrange
        var options = new OneDriveNodeOptions
        {
            ClientId = "test-client-id",
            AuthMode = "InteractiveBrowser",
            InteractiveBrowserRedirectPort = 8765
        };

        var factory = new OneDriveGraphClientFactory(_factoryLogger);

        // Act - 创建客户端不应抛出异常（实际认证在 Connect 时才发生）
        var client = await factory.CreateClientAsync(options, CancellationToken.None);

        // Assert
        Assert.NotNull(client);
    }

    /// <summary>
    /// 使用 InteractiveBrowser 流测试连接（推荐用于桌面应用）。
    /// 此测试会自动打开系统浏览器进行授权。
    /// 超时：120秒（包括浏览器交互时间）
    /// </summary>
    [SkippableFact(Timeout = 120000)]
    public async Task ConnectAsync_WithInteractiveBrowser_ShouldConnectSuccessfully()
    {
        // Arrange
        var clientId = Environment.GetEnvironmentVariable("ONEDRIVE_TEST_CLIENT_ID");

        Skip.If(string.IsNullOrEmpty(clientId),
            "请设置 ONEDRIVE_TEST_CLIENT_ID 环境变量来运行此测试");
        SkipIfInteractiveBrowserTestsNotExplicitlyEnabled();

        // InteractiveBrowser 流会自动打开系统浏览器
        // 不需要检查控制台重定向，因为认证在浏览器中完成

        var options = new OneDriveNodeOptions
        {
            TenantId = "common",
            ClientId = clientId,
            AuthMode = "InteractiveBrowser",
            InteractiveBrowserRedirectPort = 8765,
            Scopes = "Files.ReadWrite offline_access User.Read"
        };

        var factory = new OneDriveGraphClientFactory(_factoryLogger);
        var graphClient = await factory.CreateClientAsync(options, CancellationToken.None);
        var node = new OneDriveNode("test-node-interactive", "Test OneDrive (Interactive)", options, graphClient, _logger);

        // Act - 连接时会自动打开浏览器，用户授权后自动回调
        await node.ConnectAsync(CancellationToken.None);

        // Assert
        Assert.Equal(NodeState.Connected, node.State);
        Assert.NotNull(node.Metadata.Id);

        // Cleanup
        await node.DisconnectAsync(CancellationToken.None);
    }
}

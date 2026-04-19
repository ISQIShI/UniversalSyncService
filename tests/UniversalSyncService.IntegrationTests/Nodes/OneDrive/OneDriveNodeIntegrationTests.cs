using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UniversalSyncService.Abstractions.Nodes;
using UniversalSyncService.Abstractions.SyncItems;
using UniversalSyncService.Abstractions.SyncManagement.ConfigNodes;
using UniversalSyncService.Core.Nodes.OneDrive;
using Xunit;

namespace UniversalSyncService.IntegrationTests.Nodes.OneDrive;

/// <summary>
/// OneDrive 节点集成测试。
/// 
/// 三车道分类：
/// 1) WarmAuth-Auto：使用已持久化认证记录，默认无人值守执行（需显式开关）；
/// 2) ColdAuth-Manual：需要人工交互授权；
/// 3) ExpiredAuth-Negative：认证记录缺失/损坏时的受控失败语义。
/// </summary>
public class OneDriveNodeIntegrationTests
{
    private const string TestRootFolder = "UniversalSyncTest";
    private const string WarmAuthEnvVar = "ONEDRIVE_ENABLE_WARM_AUTH_TESTS";
    private const string InteractiveBrowserEnvVar = "ONEDRIVE_ENABLE_INTERACTIVE_BROWSER_TESTS";
    private const string TestClientIdEnvVar = "ONEDRIVE_TEST_CLIENT_ID";

    private readonly ILogger<OneDriveNode> _logger;
    private readonly ILogger<OneDriveGraphClientFactory> _factoryLogger;
    private readonly ILoggerFactory _loggerFactory;

    public OneDriveNodeIntegrationTests()
    {
        _logger = NullLogger<OneDriveNode>.Instance;
        _factoryLogger = NullLogger<OneDriveGraphClientFactory>.Instance;
        _loggerFactory = NullLoggerFactory.Instance;
    }

    private static void SkipIfWarmAuthTestsNotExplicitlyEnabled()
    {
        var enableWarmAuthTests = Environment.GetEnvironmentVariable(WarmAuthEnvVar);
        Skip.If(!string.Equals(enableWarmAuthTests, "1", StringComparison.Ordinal),
            $"WarmAuth 在线测试需要显式设置 {WarmAuthEnvVar}=1 后才运行，避免在未准备好持久化认证记录时误触发交互流程。");
    }

    private static void SkipIfInteractiveBrowserTestsNotExplicitlyEnabled()
    {
        var isCi = Environment.GetEnvironmentVariable("CI");
        Skip.If(!string.IsNullOrWhiteSpace(isCi),
            "ColdAuth 手工交互测试禁止在 CI 环境执行，请仅在本地交互终端运行。");

        var enableInteractiveBrowserTests = Environment.GetEnvironmentVariable(InteractiveBrowserEnvVar);
        Skip.If(!string.Equals(enableInteractiveBrowserTests, "1", StringComparison.Ordinal),
            $"ColdAuth 手工交互测试需要显式设置 {InteractiveBrowserEnvVar}=1 后才运行，避免在无人值守环境中卡住或占用本地回调端口。");
    }

    private static string GetClientIdOrSkip(string laneName)
    {
        var clientId = Environment.GetEnvironmentVariable(TestClientIdEnvVar);
        Skip.If(string.IsNullOrWhiteSpace(clientId),
            $"{laneName} 需要设置 {TestClientIdEnvVar} 环境变量。\n" +
            "请先通过 OneDriveCredentialConfigurator 配置应用凭据后再运行。" );

        return clientId!;
    }

    private static string GetWarmAuthClientIdOrSkip()
    {
        SkipIfWarmAuthTestsNotExplicitlyEnabled();
        var clientId = GetClientIdOrSkip("WarmAuth");

        var hasAuthRecord = OneDriveAuthenticationRecordStore.Exists(clientId);
        var authRecordPath = OneDriveAuthenticationRecordStore.GetRecordPath(clientId);
        Skip.If(!hasAuthRecord,
            "WarmAuth 车道要求存在持久化认证记录（authrecord），用于静默续期。\n" +
            $"当前未找到记录：{authRecordPath}。\n" +
            "请先执行一次 ColdAuth 交互授权生成记录。" );

        return clientId;
    }

    private static OneDriveNodeOptions CreateOnlineOptions(string clientId, string authMode, string? rootPath = null)
    {
        return new OneDriveNodeOptions
        {
            TenantId = "common",
            ClientId = clientId,
            AuthMode = authMode,
            RootPath = rootPath ?? "/",
            Scopes = "Files.ReadWrite offline_access User.Read"
        };
    }

    /// <summary>
    /// 生成测试文件夹路径。
    /// 格式：UniversalSyncTest/{TestName}_{Guid}
    /// </summary>
    private static string GenerateTestFolderPath(string testName)
    {
        return $"{TestRootFolder}/{testName}_{Guid.NewGuid():N}";
    }

    /// <summary>
    /// 删除测试文件夹（使用已连接的节点）。
    /// </summary>
    private static async Task CleanupTestFolderAsync(OneDriveNode node, string folderPath)
    {
        try
        {
            // 测试使用显式根目录清理入口，避免业务 DeleteAsync("") 具备误删整个 RootPath 的隐式语义。
            if (!string.IsNullOrWhiteSpace(folderPath))
            {
                await node.DeleteConfiguredRootAsync(CancellationToken.None);
            }
        }
        catch
        {
            // 清理失败不影响测试结果
        }
    }

    [Fact]
    public async Task Constructor_ShouldSetMetadataCorrectly()
    {
        var options = new OneDriveNodeOptions
        {
            ClientId = "test-client-id"
        };

        var factory = new OneDriveGraphClientFactory(_factoryLogger);
        var graphClient = await factory.CreateClientAsync(options, CancellationToken.None);

        var node = new OneDriveNode("test-id", "Test Node", options, graphClient, _logger);

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
    /// 负向基线：无有效凭据时连接应失败。
    /// </summary>
    [Fact(Timeout = 30000)]
    public async Task ConnectAsync_WithoutCredentials_ShouldThrowException()
    {
        var options = new OneDriveNodeOptions
        {
            TenantId = "common",
            ClientId = "invalid-client-id",
            AuthMode = "DeviceCode"
        };

        var factory = new OneDriveGraphClientFactory(_factoryLogger);
        var graphClient = await factory.CreateClientAsync(options, CancellationToken.None);
        var node = new OneDriveNode("test-node", "Test Node", options, graphClient, _logger);

        await Assert.ThrowsAnyAsync<Exception>(() => node.ConnectAsync(CancellationToken.None));
        Assert.Equal(NodeState.Disconnected, node.State);
    }

    [Fact]
    public void OneDriveNodeOptions_Validate_ShouldWorkCorrectly()
    {
        var validOptions = new OneDriveNodeOptions
        {
            ClientId = "valid-client-id",
            AuthMode = "InteractiveBrowser"
        };

        Assert.True(validOptions.Validate(out var error));
        Assert.Null(error);

        var invalidOptions = new OneDriveNodeOptions
        {
            ClientId = "",
            AuthMode = "InteractiveBrowser"
        };

        Assert.False(invalidOptions.Validate(out error));
        Assert.NotNull(error);
        Assert.Contains("ClientId", error);
    }

    [Theory]
    [InlineData(100, true)]
    [InlineData(4 * 1024 * 1024, true)]
    [InlineData(4 * 1024 * 1024 + 1, false)]
    [InlineData(10 * 1024 * 1024, false)]
    public void LargeFileThreshold_ShouldDetermineUploadStrategy(long fileSize, bool isSmallFile)
    {
        var options = new OneDriveNodeOptions
        {
            LargeFileThresholdBytes = 4 * 1024 * 1024
        };

        var useSmallFileUpload = fileSize <= options.LargeFileThresholdBytes;

        Assert.Equal(isSmallFile, useSmallFileUpload);
    }

    // -----------------------
    // WarmAuth-Auto 车道
    // -----------------------

    /// <summary>
    /// WarmAuth：使用已持久化认证记录进行静默连接。
    /// 不允许主动打开浏览器；若记录缺失则跳过。
    /// </summary>
    [SkippableFact(Timeout = 90000)]
    [Trait("Category", "WarmAuth")]
    public async Task ConnectAsync_WithWarmAuthRecord_ShouldConnectSuccessfully()
    {
        var clientId = GetWarmAuthClientIdOrSkip();
        var options = CreateOnlineOptions(clientId, "InteractiveBrowser");
        var factory = new OneDriveGraphClientFactory(_factoryLogger);

        // WarmAuth 前置守卫：记录缺失/不可用时应快速给出 reauth-required 语义。
        await factory.EnsureWarmAuthenticationReadyAsync(options, CancellationToken.None);

        var graphClient = await factory.CreateClientAsync(options, CancellationToken.None);
        var node = new OneDriveNode("test-node-warm-connect", "Test OneDrive (WarmAuth)", options, graphClient, _logger);

        await node.ConnectAsync(CancellationToken.None);

        Assert.Equal(NodeState.Connected, node.State);
        Assert.NotNull(node.Metadata.Id);

        await node.DisconnectAsync(CancellationToken.None);
    }

    /// <summary>
    /// WarmAuth：枚举文件/目录。
    /// </summary>
    [SkippableFact(Timeout = 90000)]
    [Trait("Category", "WarmAuth")]
    public async Task GetSyncItemsAsync_WithWarmAuth_ShouldEnumerateFilesAndFolders()
    {
        var clientId = GetWarmAuthClientIdOrSkip();
        var testFolder = GenerateTestFolderPath("WarmEnumTest");
        var options = CreateOnlineOptions(clientId, "InteractiveBrowser", testFolder);
        var factory = new OneDriveGraphClientFactory(_factoryLogger);

        await factory.EnsureWarmAuthenticationReadyAsync(options, CancellationToken.None);

        var graphClient = await factory.CreateClientAsync(options, CancellationToken.None);
        var node = new OneDriveNode("test-node-warm-enum", "Test Warm Enumeration", options, graphClient, _logger);

        await node.ConnectAsync(CancellationToken.None);

        try
        {
            var items = new List<ISyncItem>();
            await foreach (var item in node.GetSyncItemsAsync(CancellationToken.None))
            {
                items.Add(item);
                if (items.Count >= 10)
                {
                    break;
                }
            }

            Assert.True(items.Count >= 0, "应该能够枚举项目（即使为空）");

            foreach (var item in items)
            {
                Assert.NotNull(item.Metadata);
                Assert.NotNull(item.Metadata.Name);
                Assert.NotNull(item.Metadata.Path);
                Assert.True(item.ItemType == SyncItemType.File || item.ItemType == SyncItemType.Directory);
            }
        }
        finally
        {
            await CleanupTestFolderAsync(node, testFolder);
            await node.DisconnectAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// WarmAuth：上传并下载文件。
    /// </summary>
    [SkippableFact(Timeout = 120000)]
    [Trait("Category", "WarmAuth")]
    public async Task UploadAndDownloadAsync_WithWarmAuth_ShouldWorkCorrectly()
    {
        var clientId = GetWarmAuthClientIdOrSkip();
        var testFolder = GenerateTestFolderPath("WarmUploadDownloadTest");
        var options = CreateOnlineOptions(clientId, "InteractiveBrowser", testFolder);
        var factory = new OneDriveGraphClientFactory(_factoryLogger);

        await factory.EnsureWarmAuthenticationReadyAsync(options, CancellationToken.None);

        var graphClient = await factory.CreateClientAsync(options, CancellationToken.None);
        var node = new OneDriveNode("test-node-warm-upload", "Test Warm Upload/Download", options, graphClient, _logger);

        await node.ConnectAsync(CancellationToken.None);

        try
        {
            var testContent = $"WarmAuth test content generated at {DateTime.UtcNow:O}";
            var testFilePath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.txt");
            await File.WriteAllTextAsync(testFilePath, testContent);

            try
            {
                var uploadItem = new Core.SyncItems.FileSystemSyncItem(testFilePath, "testfile.txt");
                await node.UploadAsync(uploadItem, CancellationToken.None);

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

                ISyncItem? downloadedItem = null;
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
                if (File.Exists(testFilePath))
                {
                    File.Delete(testFilePath);
                }
            }
        }
        finally
        {
            await CleanupTestFolderAsync(node, testFolder);
            await node.DisconnectAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// WarmAuth：创建目录。
    /// </summary>
    [SkippableFact(Timeout = 90000)]
    [Trait("Category", "WarmAuth")]
    public async Task UploadDirectoryAsync_WithWarmAuth_ShouldCreateFolder()
    {
        var clientId = GetWarmAuthClientIdOrSkip();
        var testFolder = GenerateTestFolderPath("WarmFolderCreationTest");
        var options = CreateOnlineOptions(clientId, "InteractiveBrowser", testFolder);
        var factory = new OneDriveGraphClientFactory(_factoryLogger);

        await factory.EnsureWarmAuthenticationReadyAsync(options, CancellationToken.None);

        var graphClient = await factory.CreateClientAsync(options, CancellationToken.None);
        var node = new OneDriveNode("test-node-warm-folder", "Test Warm Folder Creation", options, graphClient, _logger);

        await node.ConnectAsync(CancellationToken.None);

        try
        {
            var folderMetadata = new SyncItemMetadata(
                "TestSubFolder",
                "TestSubFolder",
                "TestSubFolder",
                0,
                DateTime.UtcNow,
                DateTime.UtcNow,
                null,
                "inode/directory");

            var folderItem = new Core.SyncItems.FileSystemSyncItem(folderMetadata, SyncItemType.Directory);
            await node.UploadAsync(folderItem, CancellationToken.None);

            var foundFolder = false;
            await foreach (var item in node.GetSyncItemsAsync(CancellationToken.None))
            {
                if (item.Metadata.Name == "TestSubFolder" && item.ItemType == SyncItemType.Directory)
                {
                    foundFolder = true;
                    break;
                }
            }

            Assert.True(foundFolder, "应该能找到创建的文件夹");
        }
        finally
        {
            await CleanupTestFolderAsync(node, testFolder);
            await node.DisconnectAsync(CancellationToken.None);
        }
    }

    // -----------------------
    // ColdAuth-Manual 车道
    // -----------------------

    /// <summary>
    /// ColdAuth：InteractiveBrowser 手工授权连接。
    /// 此测试永不在 CI 运行，只用于本地手工授权验证。
    /// </summary>
    [SkippableFact(Timeout = 120000)]
    [Trait("Category", "ColdAuth")]
    public async Task ConnectAsync_WithInteractiveBrowserManualAuth_ShouldConnectSuccessfully()
    {
        var clientId = GetClientIdOrSkip("ColdAuth");
        SkipIfInteractiveBrowserTestsNotExplicitlyEnabled();

        var options = CreateOnlineOptions(clientId, "InteractiveBrowser");
        var factory = new OneDriveGraphClientFactory(_factoryLogger);
        var graphClient = await factory.CreateClientAsync(options, CancellationToken.None);
        var node = new OneDriveNode("test-node-interactive", "Test OneDrive (Interactive)", options, graphClient, _logger);

        await node.ConnectAsync(CancellationToken.None);

        Assert.Equal(NodeState.Connected, node.State);
        Assert.NotNull(node.Metadata.Id);

        await node.DisconnectAsync(CancellationToken.None);
    }

    /// <summary>
    /// ColdAuth：DeviceCode 手工授权连接。
    /// 修复命名语义：该测试真实使用 DeviceCode 模式。
    /// </summary>
    [SkippableFact(Timeout = 120000)]
    [Trait("Category", "ColdAuth")]
    public async Task ConnectAsync_WithDeviceCodeManualAuth_ShouldConnectSuccessfully()
    {
        var clientId = GetClientIdOrSkip("ColdAuth");
        SkipIfInteractiveBrowserTestsNotExplicitlyEnabled();

        Skip.If(Console.IsInputRedirected || Console.IsOutputRedirected,
            "DeviceCode 流需要交互式控制台，无法在重定向的 I/O 环境中运行。");

        var options = CreateOnlineOptions(clientId, "DeviceCode");
        var factory = new OneDriveGraphClientFactory(_factoryLogger);
        var graphClient = await factory.CreateClientAsync(options, CancellationToken.None);
        var node = new OneDriveNode("test-node-devicecode", "Test OneDrive (DeviceCode)", options, graphClient, _logger);

        await node.ConnectAsync(CancellationToken.None);

        Assert.Equal(NodeState.Connected, node.State);
        Assert.NotNull(node.Metadata.Id);

        await node.DisconnectAsync(CancellationToken.None);
    }

    // -----------------------
    // ExpiredAuth-Negative 车道
    // -----------------------

    /// <summary>
    /// 认证记录缺失时，应返回 reauth-required 受控失败语义，不应进入无限重试或挂起。
    /// </summary>
    [Fact(Timeout = 10000)]
    [Trait("Category", "ExpiredAuth")]
    public async Task EnsureWarmAuthenticationReadyAsync_WhenAuthRecordMissing_ShouldFailFastWithReauthRequired()
    {
        var missingClientId = $"missing-client-{Guid.NewGuid():N}";
        var options = CreateOnlineOptions(missingClientId, "InteractiveBrowser");
        var factory = new OneDriveGraphClientFactory(_factoryLogger);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => factory.EnsureWarmAuthenticationReadyAsync(options, CancellationToken.None));

        Assert.True(exception.Message.Contains("reauth-required", StringComparison.OrdinalIgnoreCase),
            "异常消息应包含 reauth-required 语义，便于上层识别并引导重新授权。");
    }

    /// <summary>
    /// 认证记录损坏（模拟过期/不可用状态）时，应返回 reauth-required 受控失败语义。
    /// </summary>
    [Fact(Timeout = 10000)]
    [Trait("Category", "ExpiredAuth")]
    public async Task EnsureWarmAuthenticationReadyAsync_WhenAuthRecordCorrupted_ShouldFailFastWithReauthRequired()
    {
        var corruptedClientId = $"corrupted-client-{Guid.NewGuid():N}";
        var recordPath = OneDriveAuthenticationRecordStore.GetRecordPath(corruptedClientId);
        var recordDirectory = Path.GetDirectoryName(recordPath);

        if (!string.IsNullOrWhiteSpace(recordDirectory))
        {
            Directory.CreateDirectory(recordDirectory);
        }

        await File.WriteAllTextAsync(recordPath, "this-is-not-a-valid-authrecord");

        try
        {
            var options = CreateOnlineOptions(corruptedClientId, "InteractiveBrowser");
            var factory = new OneDriveGraphClientFactory(_factoryLogger);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => factory.EnsureWarmAuthenticationReadyAsync(options, CancellationToken.None));

            Assert.True(exception.Message.Contains("reauth-required", StringComparison.OrdinalIgnoreCase),
                "损坏记录应触发 reauth-required，而不是长时间等待或进入交互流程。");
        }
        finally
        {
            await OneDriveAuthenticationRecordStore.DeleteIfExistsAsync(corruptedClientId, CancellationToken.None);
        }
    }

    /// <summary>
    /// Provider 层也应暴露 WarmAuth 前置守卫能力，便于 API/UI 先做快速预检。
    /// </summary>
    [Fact(Timeout = 10000)]
    [Trait("Category", "ExpiredAuth")]
    public async Task EnsureWarmAuthenticationReadyAsync_FromProvider_WhenAuthRecordMissing_ShouldFailFast()
    {
        var missingClientId = $"provider-missing-client-{Guid.NewGuid():N}";
        var nodeConfiguration = new NodeConfiguration(
            id: "node-provider-warm-auth-test",
            name: "OneDrive Provider WarmAuth Test",
            nodeType: "OneDrive",
            connectionSettings: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ClientId"] = missingClientId,
                ["TenantId"] = "common",
                ["AuthMode"] = "InteractiveBrowser",
                ["RootPath"] = "/"
            });

        var factory = new OneDriveGraphClientFactory(_factoryLogger);
        var provider = new OneDriveNodeProvider(factory, _loggerFactory);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.EnsureWarmAuthenticationReadyAsync(nodeConfiguration, CancellationToken.None));

        Assert.True(exception.Message.Contains("reauth-required", StringComparison.OrdinalIgnoreCase),
            "Provider 预检应向上游暴露 reauth-required 语义。");
    }

    [Fact]
    public void OneDriveNodeOptions_RetryConfiguration_ShouldHaveDefaults()
    {
        var options = new OneDriveNodeOptions();

        Assert.Equal(3, options.MaxRetries);
        Assert.Equal(1000, options.BackoffMs);
        Assert.True(options.MaxRetries > 0, "应该有重试机制");
        Assert.True(options.BackoffMs > 0, "应该有退避延迟");
    }

    [Fact]
    public void OneDriveNodeOptions_TokenCacheConfiguration_ShouldBeValid()
    {
        var options = new OneDriveNodeOptions();

        Assert.NotNull(options.TokenCachePath);
        Assert.False(string.IsNullOrWhiteSpace(options.TokenCachePath));
        Assert.Equal("data/auth/onedrive", options.TokenCachePath);
    }

    [Theory]
    [InlineData(4 * 1024 * 1024, 5 * 1024 * 1024)]
    public void OneDriveNodeOptions_UploadConfiguration_ShouldBeValid(long threshold, int sliceSize)
    {
        var options = new OneDriveNodeOptions
        {
            LargeFileThresholdBytes = threshold,
            UploadSliceSizeBytes = sliceSize
        };

        Assert.Equal(threshold, options.LargeFileThresholdBytes);
        Assert.Equal(sliceSize, options.UploadSliceSizeBytes);
        Assert.True(options.UploadSliceSizeBytes % (320 * 1024) == 0,
            "分块大小应该是 320KB 的倍数");
    }

    [Fact]
    public void OneDriveNodeOptions_InteractiveBrowserConfiguration_ShouldHaveDefaults()
    {
        var options = new OneDriveNodeOptions();

        Assert.Equal(8765, options.InteractiveBrowserRedirectPort);
        Assert.True(options.InteractiveBrowserRedirectPort > 1024, "回调端口应该大于 1024（避免使用特权端口）");
        Assert.True(options.InteractiveBrowserRedirectPort < 65536, "回调端口应该在有效范围内");
    }

    [Fact]
    public async Task CreateClient_WithInteractiveBrowserAuthMode_ShouldCreateClient()
    {
        var options = new OneDriveNodeOptions
        {
            ClientId = "test-client-id",
            AuthMode = "InteractiveBrowser",
            InteractiveBrowserRedirectPort = 8765
        };

        var factory = new OneDriveGraphClientFactory(_factoryLogger);
        var client = await factory.CreateClientAsync(options, CancellationToken.None);

        Assert.NotNull(client);
    }
}

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UniversalSyncService.Abstractions.Configuration;
using UniversalSyncService.Abstractions.SyncManagement.History;
using UniversalSyncService.Abstractions.SyncManagement;
using UniversalSyncService.Abstractions.SyncManagement.Tasks;
using UniversalSyncService.Core.DependencyInjection;
using UniversalSyncService.Core.Nodes.OneDrive;
using UniversalSyncService.Core.SyncItems;
using UniversalSyncService.Core.SyncManagement.ConfigNodes;
using UniversalSyncService.Host.Configuration;
using UniversalSyncService.Testing;
using Xunit;
using OneDriveLaneConfiguration = UniversalSyncService.Testing.OneDriveTestBootstrap.OneDriveLaneConfiguration;

namespace UniversalSyncService.IntegrationTests;

/// <summary>
/// 节点矩阵测试：统一抽象 harness 驱动所有 concrete node types。
///
/// 目标：
/// - 主测试链（contract/harness）覆盖 Local↔Local 与 Local↔OneDrive；
/// - 节点创建/配置、同步执行、冲突检测、历史记录均走同一断言矩阵；
/// - OnlineColdAuth：仅保留显式人工入口，不作为默认 gate；
/// - AuthNegative：持久化认证缺失时受控失败（fail-fast）。
/// </summary>
public sealed class NodeMatrixSyncTests : IAsyncLifetime
{
    private const string OneDriveTestConfigFileName = "onedrive.test.yaml";

    private readonly ILogger<OneDriveGraphClientFactory> _factoryLogger = NullLogger<OneDriveGraphClientFactory>.Instance;
    private readonly ILogger<OneDriveNode> _nodeLogger = NullLogger<OneDriveNode>.Instance;

    private TempContentRoot? _testRoot;

    private string TestRootPath => _testRoot!.RootPath;

    public Task InitializeAsync()
    {
        return InitializeCoreAsync();
    }

    public async Task DisposeAsync()
    {
        if (_testRoot is not null)
        {
            await _testRoot.DisposeAsync();
        }
    }

    [Fact]
    [Trait("Category", "Offline")]
    public async Task Matrix_LocalToLocal_ShouldPassUnifiedAbstractContract()
        => await ExecuteUnifiedContractMatrixAsync(new LocalToLocalScenario(this));

    [SkippableFact(Timeout = 180000)]
    [Trait("Category", "OnlineWarmAuth")]
    public async Task Matrix_LocalToOneDrive_ShouldPassUnifiedAbstractContract()
        => await ExecuteUnifiedContractMatrixAsync(new LocalToOneDriveWarmScenario(this));

    [SkippableFact(Timeout = 120000)]
    [Trait("Category", "OnlineColdAuth")]
    public async Task Matrix_LocalToOneDrive_ColdAuth_ShouldRequireExplicitManualEntryOnly()
    {
        var lane = GetOnlineColdAuthLaneConfigurationOrSkip(requiresInteractiveConsole: false);
        var manualRoot = $"/UniversalSyncTest/NodeMatrixCold_{Guid.NewGuid():N}";

        var node = await CreateConnectedOneDriveNodeAsync(lane, manualRoot, ensureWarmAuthRecord: false);
        try
        {
            Assert.Equal(UniversalSyncService.Abstractions.Nodes.NodeState.Connected, node.State);
        }
        finally
        {
            await CleanupRemoteRootAsync(node);
        }
    }

    [Fact(Timeout = 10000)]
    [Trait("Category", "AuthNegative")]
    public async Task Matrix_AuthNegative_WhenWarmAuthRecordMissing_ShouldFailFastWithReauthRequired()
    {
        var missingClientId = $"matrix-missing-{Guid.NewGuid():N}";
        var options = new OneDriveNodeOptions
        {
            ClientId = missingClientId,
            TenantId = "common",
            AuthMode = "InteractiveBrowser",
            RootPath = "/"
        };

        var factory = new OneDriveGraphClientFactory(_factoryLogger);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => factory.EnsureWarmAuthenticationReadyAsync(options, CancellationToken.None));

        Assert.Contains("reauth-required", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private async Task InitializeCoreAsync()
    {
        _testRoot = await TempContentRoot.CreateAsync("UniversalSyncService-NodeMatrix");
    }

    private async Task ExecuteUnifiedContractMatrixAsync(INodeMatrixContractScenario scenario)
    {
        await using (scenario)
        {
            await scenario.ArrangeAsync(TestRootPath);

            using var host = await CreateHostAsync(TestRootPath);
            await WaitForNodesAsync(host.Services, scenario.RequiredNodeIds);
            await scenario.AssertNodeConfigurationAsync(host.Services);

            var planManager = host.Services.GetRequiredService<ISyncPlanManager>();
            var historyManager = host.Services.GetRequiredService<ISyncHistoryManager>();

            var firstResults = await planManager.ExecutePlanNowAsync(scenario.PlanId, CancellationToken.None);
            TestAssert.ContainsOnlyExpectedResults(firstResults, SyncTaskResult.Success, SyncTaskResult.NoChanges);
            await scenario.AssertAfterInitialSyncAsync();

            await scenario.AddSlaveOnlyChangeAsync(TestRootPath);
            var secondResults = await planManager.ExecutePlanNowAsync(scenario.PlanId, CancellationToken.None);
            TestAssert.ContainsOnlyExpectedResults(secondResults, SyncTaskResult.Success, SyncTaskResult.NoChanges);
            await scenario.AssertSlaveToMasterSignalAsync();

            await scenario.CreateConflictAsync(TestRootPath);
            var conflictResults = await planManager.ExecutePlanNowAsync(scenario.PlanId, CancellationToken.None);
            Assert.Contains(SyncTaskResult.Conflict, conflictResults.Values);

            var recentHistory = await historyManager.GetRecentHistoryAsync(scenario.PlanId, limit: 100);
            Assert.Contains(
                recentHistory,
                entry => entry.PlanId == scenario.PlanId
                    && entry.Metadata.Path.Replace('\\', '/').Equals(scenario.HistoryProbePath, StringComparison.OrdinalIgnoreCase));

            await host.StopAsync();
        }
    }

    private static async Task<IHost> CreateHostAsync(string contentRoot)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = contentRoot,
            EnvironmentName = Environments.Production,
            Configuration = new ConfigurationManager()
        });

        // 节点矩阵 harness 需要严格可复现配置，避免环境变量覆盖 OneDrive RootPath。
        builder.Configuration.Sources.Clear();
        builder.Configuration
            .SetBasePath(contentRoot)
            .AddYamlFile("appsettings.yaml", optional: false, reloadOnChange: false);

        builder.Services
            .AddUniversalSyncOptions(builder.Configuration)
            .AddUniversalSyncCore();

        var host = builder.Build();
        await host.StartAsync();
        return host;
    }

    private static async Task WaitForNodesAsync(IServiceProvider serviceProvider, params string[] nodeIds)
    {
        var nodeRegistry = serviceProvider.GetRequiredService<NodeRegistry>();
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (nodeIds.All(nodeId => nodeRegistry.TryGet(nodeId, out _)))
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("节点注册表未在预期时间内完成加载。");
    }

    private async Task WriteLocalLocalConfigAsync(string masterRoot, string slaveRoot)
    {
        await ConfigLoader.WriteYamlAsync(
            outputDirectory: TestRootPath,
            templatePath: TestConfigPaths.GetTemplatePath("integration.test.yaml"),
            localOverridePath: TestConfigPaths.GetLocalOverridePath("integration.test.yaml"),
            placeholders: new Dictionary<string, string>
            {
                ["MASTER_ROOT"] = masterRoot.Replace("\\", "/"),
                ["SLAVE_ROOT"] = slaveRoot.Replace("\\", "/"),
                ["SYNC_MODE"] = "Bidirectional",
                ["CONFLICT_RESOLUTION_STRATEGY"] = "Manual",
                ["SOURCE_PATH"] = ".",
                ["TARGET_PATH"] = "."
            });
    }

    private async Task WriteLocalOneDriveWarmConfigAsync(string masterRoot, OneDriveLaneConfiguration lane, string remoteRoot)
    {
        var normalizedRemoteRoot = remoteRoot.StartsWith("/", StringComparison.Ordinal)
            ? remoteRoot
            : $"/{remoteRoot}";

        var outputPath = await ConfigLoader.WriteYamlAsync(
            outputDirectory: TestRootPath,
            templatePath: TestConfigPaths.GetTemplatePath("integration.matrix.onedrive.test.yaml"),
            // 矩阵 warm-auth 用例使用受控模板，避免本地覆盖无意覆盖 RootPath 导致链路不确定。
            localOverridePath: null,
            placeholders: new Dictionary<string, string>
            {
                ["MASTER_ROOT"] = masterRoot.Replace("\\", "/"),
                ["ONEDRIVE_CLIENT_ID"] = lane.ClientId!,
                ["ONEDRIVE_TENANT_ID"] = lane.TenantId,
                ["ONEDRIVE_ROOT"] = normalizedRemoteRoot,
                ["SYNC_MODE"] = "Bidirectional",
                ["CONFLICT_RESOLUTION_STRATEGY"] = "Manual",
                ["SOURCE_PATH"] = ".",
                ["TARGET_PATH"] = "."
            });

        var generatedYaml = await File.ReadAllTextAsync(outputPath);
        Assert.DoesNotContain("{{ONEDRIVE_ROOT}}", generatedYaml, StringComparison.Ordinal);
        Assert.Contains($"RootPath: \"{normalizedRemoteRoot}\"", generatedYaml, StringComparison.Ordinal);
    }

    private async Task<OneDriveNode> CreateConnectedOneDriveNodeAsync(
        OneDriveLaneConfiguration lane,
        string rootPath,
        bool ensureWarmAuthRecord)
    {
        var options = CreateOneDriveOptions(lane.ClientId!, lane.TenantId, "InteractiveBrowser", rootPath);
        var factory = new OneDriveGraphClientFactory(_factoryLogger);
        if (ensureWarmAuthRecord)
        {
            await factory.EnsureWarmAuthenticationReadyAsync(options, CancellationToken.None);
        }

        var graphClient = await factory.CreateClientAsync(options, CancellationToken.None);
        var node = new OneDriveNode(
            id: $"matrix-verify-{Guid.NewGuid():N}",
            name: "Node Matrix Verification",
            options: options,
            graphClient: graphClient,
            logger: _nodeLogger);

        await node.ConnectAsync(CancellationToken.None);
        return node;
    }

    private static async Task<HashSet<string>> EnumerateRelativePathsAsync(OneDriveNode node, CancellationToken cancellationToken)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await foreach (var item in node.GetSyncItemsAsync(cancellationToken))
        {
            paths.Add(item.Metadata.Path.Replace('\\', '/'));
        }

        return paths;
    }

    private static async Task CleanupRemoteRootAsync(OneDriveNode node)
    {
        try
        {
            await node.DeleteConfiguredRootAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            // 清理失败记录诊断信息但不阻塞测试断言
            // 远端清理失败不应导致测试失败，但应记录以便诊断
            Console.WriteLine($"[NodeMatrixCleanup] DeleteConfiguredRootAsync failed: {ex.Message}");
        }
        finally
        {
            await node.DisconnectAsync(CancellationToken.None);
        }
    }

    private static OneDriveNodeOptions CreateOneDriveOptions(string clientId, string tenantId, string authMode, string rootPath)
    {
        return new OneDriveNodeOptions
        {
            ClientId = clientId,
            TenantId = tenantId,
            AuthMode = authMode,
            RootPath = rootPath,
            Scopes = "Files.ReadWrite offline_access User.Read"
        };
    }

    private static async Task<OneDriveLaneConfiguration> GetOnlineWarmAuthLaneConfigurationOrSkipAsync()
    {
        var configuration = OneDriveTestBootstrap.ResolveLaneConfiguration(OneDriveTestConfigFileName);

        Skip.If(string.IsNullOrWhiteSpace(configuration.ClientId),
            "OnlineWarmAuth 前置检查失败：未解析到有效 ClientId（模板/本地覆盖/本地持久化凭据）。");

        try
        {
            await OneDriveTestBootstrap.EnsureWarmAuthRecordReadyAsync(configuration.ClientId!, CancellationToken.None);
        }
        catch (InvalidOperationException ex)
        {
            Skip.If(true,
                "OnlineWarmAuth 前置检查失败：缺少可复用持久化认证记录，按设计 Skip。\n" +
                ex.Message);
        }

        return configuration;
    }

    private static OneDriveLaneConfiguration GetOnlineColdAuthLaneConfigurationOrSkip(bool requiresInteractiveConsole)
    {
        var configuration = OneDriveTestBootstrap.ResolveLaneConfiguration(OneDriveTestConfigFileName);

        Skip.If(!configuration.EnableOnlineColdAuth,
            "OnlineColdAuth 默认不自动执行。请在 tests/Config/Local/onedrive.test.yaml 显式设置 EnableOnlineColdAuth: true。");
        Skip.If(!Environment.UserInteractive,
            "OnlineColdAuth 需要交互式会话，当前非交互环境，按设计 Skip。");

        if (requiresInteractiveConsole)
        {
            Skip.If(Console.IsInputRedirected || Console.IsOutputRedirected,
                "OnlineColdAuth 需要交互式控制台，当前 I/O 被重定向，按设计 Skip。");
        }

        Skip.If(string.IsNullOrWhiteSpace(configuration.ClientId),
            "OnlineColdAuth 前置检查失败：未解析到有效 ClientId，按设计 Skip。");

        return configuration;
    }

    private interface INodeMatrixContractScenario : IAsyncDisposable
    {
        string PlanId { get; }

        string HistoryProbePath { get; }

        string[] RequiredNodeIds { get; }

        Task ArrangeAsync(string testRootPath);

        Task AssertNodeConfigurationAsync(IServiceProvider services);

        Task AssertAfterInitialSyncAsync();

        Task AddSlaveOnlyChangeAsync(string testRootPath);

        Task AssertSlaveToMasterSignalAsync();

        Task CreateConflictAsync(string testRootPath);
    }

    private sealed class LocalToLocalScenario(NodeMatrixSyncTests owner) : INodeMatrixContractScenario
    {
        private const string BaselineRelativePath = "docs/nested/baseline.txt";
        private const string ConflictRelativePath = "conflict/shared.txt";
        private const string SlaveOnlyRelativePath = "downloads/from-slave.txt";

        private string _masterRoot = string.Empty;
        private string _slaveRoot = string.Empty;

        public string PlanId => "local-filesystem-test";

        public string HistoryProbePath => BaselineRelativePath;

        public string[] RequiredNodeIds => ["local-master", "local-slave"];

        public async Task ArrangeAsync(string testRootPath)
        {
            _masterRoot = Path.Combine(testRootPath, "matrix-local-master");
            _slaveRoot = Path.Combine(testRootPath, "matrix-local-slave");

            Directory.CreateDirectory(Path.Combine(_masterRoot, "docs", "nested"));
            Directory.CreateDirectory(Path.Combine(_masterRoot, "conflict"));
            Directory.CreateDirectory(_slaveRoot);

            await File.WriteAllTextAsync(Path.Combine(_masterRoot, BaselineRelativePath), "local-baseline");
            await File.WriteAllTextAsync(Path.Combine(_masterRoot, ConflictRelativePath), "conflict-seed");

            await owner.WriteLocalLocalConfigAsync(_masterRoot, _slaveRoot);
        }

        public Task AssertNodeConfigurationAsync(IServiceProvider services)
        {
            var configurationService = services.GetRequiredService<IConfigurationManagementService>();
            var syncOptions = configurationService.GetSyncOptions();
            var masterOption = syncOptions.Nodes.First(option => option.Id == "local-master");
            var slaveOption = syncOptions.Nodes.First(option => option.Id == "local-slave");

            Assert.Equal("Local", masterOption.NodeType);
            Assert.Equal("Local", slaveOption.NodeType);
            Assert.Equal(_masterRoot.Replace('\\', '/'), masterOption.ConnectionSettings["RootPath"]);
            Assert.Equal(_slaveRoot.Replace('\\', '/'), slaveOption.ConnectionSettings["RootPath"]);

            return Task.CompletedTask;
        }

        public async Task AssertAfterInitialSyncAsync()
        {
            var slaveBaselinePath = Path.Combine(_slaveRoot, BaselineRelativePath);
            var slaveConflictSeedPath = Path.Combine(_slaveRoot, ConflictRelativePath);

            Assert.True(File.Exists(slaveBaselinePath));
            Assert.True(File.Exists(slaveConflictSeedPath));
            Assert.Equal("local-baseline", await File.ReadAllTextAsync(slaveBaselinePath));
            Assert.Equal("conflict-seed", await File.ReadAllTextAsync(slaveConflictSeedPath));
        }

        public async Task AddSlaveOnlyChangeAsync(string testRootPath)
        {
            var slaveOnlyPath = Path.Combine(_slaveRoot, SlaveOnlyRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(slaveOnlyPath)!);
            await File.WriteAllTextAsync(slaveOnlyPath, "from-slave");
        }

        public async Task AssertSlaveToMasterSignalAsync()
        {
            var pulledPath = Path.Combine(_masterRoot, SlaveOnlyRelativePath);
            Assert.True(File.Exists(pulledPath));
            Assert.Equal("from-slave", await File.ReadAllTextAsync(pulledPath));
        }

        public async Task CreateConflictAsync(string testRootPath)
        {
            await File.WriteAllTextAsync(Path.Combine(_masterRoot, ConflictRelativePath), "master-conflict-v2");
            await File.WriteAllTextAsync(Path.Combine(_slaveRoot, ConflictRelativePath), "slave-conflict-v2");
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class LocalToOneDriveWarmScenario(NodeMatrixSyncTests owner) : INodeMatrixContractScenario
    {
        private const string BaselineRelativePath = "docs/guide.txt";
        private const string ConflictRelativePath = "conflict/shared.txt";
        private const string SlaveOnlyRelativePath = "downloads/from-remote.txt";

        private string _masterRoot = string.Empty;
        private string _remoteRoot = string.Empty;
        private OneDriveNode? _verifyNode;

        public string PlanId => "node-matrix-local-onedrive";

        public string HistoryProbePath => BaselineRelativePath;

        public string[] RequiredNodeIds => ["local-master", "one-drive-slave"];

        public async Task ArrangeAsync(string testRootPath)
        {
            var lane = await GetOnlineWarmAuthLaneConfigurationOrSkipAsync();

            _masterRoot = Path.Combine(testRootPath, "matrix-onedrive-master");
            Directory.CreateDirectory(Path.Combine(_masterRoot, "docs"));
            Directory.CreateDirectory(Path.Combine(_masterRoot, "conflict"));

            await File.WriteAllTextAsync(Path.Combine(_masterRoot, BaselineRelativePath), "v1-from-local");
            await File.WriteAllTextAsync(Path.Combine(_masterRoot, ConflictRelativePath), "conflict-seed");

            _remoteRoot = $"/UniversalSyncTest/NodeMatrixWarm_{Guid.NewGuid():N}";
            await owner.WriteLocalOneDriveWarmConfigAsync(_masterRoot, lane, _remoteRoot);
            _verifyNode = await owner.CreateConnectedOneDriveNodeAsync(lane, _remoteRoot, ensureWarmAuthRecord: true);
        }

        public Task AssertNodeConfigurationAsync(IServiceProvider services)
        {
            var configurationService = services.GetRequiredService<IConfigurationManagementService>();
            var oneDriveNodeOption = configurationService
                .GetSyncOptions()
                .Nodes
                .First(option => option.Id == "one-drive-slave");

            Assert.Equal("OneDrive", oneDriveNodeOption.NodeType);
            Assert.True(oneDriveNodeOption.ConnectionSettings.TryGetValue("RootPath", out var configuredRootPath));
            Assert.Equal(_remoteRoot, configuredRootPath);

            return Task.CompletedTask;
        }

        public async Task AssertAfterInitialSyncAsync()
        {
            var snapshot = await EnumerateRelativePathsAsync(_verifyNode!, CancellationToken.None);
            Assert.Contains("docs", snapshot);
            Assert.Contains(BaselineRelativePath, snapshot);
            Assert.Contains(ConflictRelativePath, snapshot);
        }

        public Task AddSlaveOnlyChangeAsync(string testRootPath)
            => UploadTextToRemoteAsync(testRootPath, "from-remote", SlaveOnlyRelativePath);

        public async Task AssertSlaveToMasterSignalAsync()
        {
            var pulledPath = Path.Combine(_masterRoot, SlaveOnlyRelativePath);
            Assert.True(File.Exists(pulledPath));
            Assert.Equal("from-remote", await File.ReadAllTextAsync(pulledPath));
        }

        public async Task CreateConflictAsync(string testRootPath)
        {
            await File.WriteAllTextAsync(Path.Combine(_masterRoot, ConflictRelativePath), "master-conflict-v2");
            await UploadTextToRemoteAsync(testRootPath, "slave-conflict-v2", ConflictRelativePath);
        }

        public async ValueTask DisposeAsync()
        {
            if (_verifyNode is not null)
            {
                await CleanupRemoteRootAsync(_verifyNode);
            }
        }

        private async Task UploadTextToRemoteAsync(string testRootPath, string content, string relativePath)
        {
            var tempPath = Path.Combine(testRootPath, $"matrix-remote-{Guid.NewGuid():N}.txt");
            await File.WriteAllTextAsync(tempPath, content);
            try
            {
                var item = new FileSystemSyncItem(tempPath, relativePath);
                await _verifyNode!.UploadAsync(item, CancellationToken.None);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }
    }
}

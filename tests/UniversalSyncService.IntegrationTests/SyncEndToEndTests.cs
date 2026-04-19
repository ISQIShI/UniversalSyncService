using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using UniversalSyncService.Abstractions.SyncManagement;
using UniversalSyncService.Abstractions.SyncManagement.Engine;
using UniversalSyncService.Abstractions.SyncManagement.Tasks;
using UniversalSyncService.Core.DependencyInjection;
using UniversalSyncService.Core.SyncManagement.ConfigNodes;
using UniversalSyncService.Host.Configuration;
using UniversalSyncService.Testing;
using Xunit;

namespace UniversalSyncService.IntegrationTests;

public sealed class SyncEndToEndTests : IAsyncLifetime
{
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
    public async Task LocalFilesystemPlan_Should_CopyFile_And_WriteSqliteHistory()
    {
        var masterRoot = Path.Combine(TestRootPath, "master");
        var slaveRoot = Path.Combine(TestRootPath, "slave");
        Directory.CreateDirectory(masterRoot);
        Directory.CreateDirectory(slaveRoot);

        var sourceFilePath = Path.Combine(masterRoot, "hello.txt");
        await File.WriteAllTextAsync(sourceFilePath, "hello-sync");

        await WriteLocalLocalConfigAsync(masterRoot, slaveRoot);

        using var host = await CreateHostAsync(TestRootPath);
        await WaitForNodesAsync(host.Services, "local-master", "local-slave");
        var planManager = host.Services.GetRequiredService<ISyncPlanManager>();

        var plans = planManager.GetAllPlans();
        Assert.Single(plans);
        Assert.Equal("local-filesystem-test", plans[0].Id);

        var results = await planManager.ExecutePlanNowAsync("local-filesystem-test", CancellationToken.None);
        TestAssert.ContainsOnlyExpectedResults(results, SyncTaskResult.Success, SyncTaskResult.NoChanges);

        var targetFilePath = Path.Combine(slaveRoot, "hello.txt");
        Assert.True(File.Exists(targetFilePath));
        Assert.Equal("hello-sync", await File.ReadAllTextAsync(targetFilePath));

        var sqlitePath = Path.Combine(TestRootPath, "data", "sync-history.db");
        Assert.True(File.Exists(sqlitePath));

        var executedPlan = planManager.GetPlanById("local-filesystem-test");
        Assert.NotNull(executedPlan);
        Assert.True(executedPlan.ExecutionCount >= 1);

        await host.StopAsync();
    }

    [Fact]
    [Trait("Category", "Offline")]
    public async Task LocalFilesystemPlan_Should_SyncNestedDirectories()
    {
        var masterRoot = Path.Combine(TestRootPath, "master-update");
        var slaveRoot = Path.Combine(TestRootPath, "slave-update");
        Directory.CreateDirectory(Path.Combine(masterRoot, "docs", "nested"));
        Directory.CreateDirectory(slaveRoot);

        var masterFilePath = Path.Combine(masterRoot, "docs", "nested", "note.txt");
        var slaveFilePath = Path.Combine(slaveRoot, "docs", "nested", "note.txt");
        await File.WriteAllTextAsync(masterFilePath, "v1");

        await WriteLocalLocalConfigAsync(masterRoot, slaveRoot);

        using var host = await CreateHostAsync(TestRootPath);
        await WaitForNodesAsync(host.Services, "local-master", "local-slave");
        var planManager = host.Services.GetRequiredService<ISyncPlanManager>();

        var results = await planManager.ExecutePlanNowAsync("local-filesystem-test", CancellationToken.None);
        Assert.NotEmpty(results);

        Assert.True(File.Exists(slaveFilePath));
        Assert.Equal("v1", await File.ReadAllTextAsync(slaveFilePath));

        await host.StopAsync();
    }

    [Fact]
    [Trait("Category", "Offline")]
    public async Task LocalFilesystemPlan_Should_ReturnConflict_WhenBothSidesChangeAfterHistoryCreated()
    {
        var masterRoot = Path.Combine(TestRootPath, "master-conflict");
        var slaveRoot = Path.Combine(TestRootPath, "slave-conflict");
        Directory.CreateDirectory(masterRoot);
        Directory.CreateDirectory(slaveRoot);

        var masterFilePath = Path.Combine(masterRoot, "conflict.txt");
        var slaveFilePath = Path.Combine(slaveRoot, "conflict.txt");
        await File.WriteAllTextAsync(masterFilePath, "base-version");

        await WriteLocalLocalConfigAsync(masterRoot, slaveRoot);

        using var host = await CreateHostAsync(TestRootPath);
        await WaitForNodesAsync(host.Services, "local-master", "local-slave");
        var planManager = host.Services.GetRequiredService<ISyncPlanManager>();

        // 先执行一次，建立双方一致的历史基线。
        await planManager.ExecutePlanNowAsync("local-filesystem-test", CancellationToken.None);

        await Task.Delay(1100);
        await File.WriteAllTextAsync(masterFilePath, "master-version");
        await Task.Delay(1100);
        await File.WriteAllTextAsync(slaveFilePath, "slave-version");

        var results = await planManager.ExecutePlanNowAsync("local-filesystem-test", CancellationToken.None);
        Assert.NotEmpty(results);
        Assert.Contains(SyncTaskResult.Conflict, results.Values);

        Assert.Equal("master-version", await File.ReadAllTextAsync(masterFilePath));
        Assert.Equal("slave-version", await File.ReadAllTextAsync(slaveFilePath));

        await host.StopAsync();
    }

    [Fact]
    [Trait("Category", "Offline")]
    public async Task LocalFilesystemPlan_Should_DeleteRemoteFile_InPushAndDeleteMode()
    {
        var masterRoot = Path.Combine(TestRootPath, "master-delete");
        var slaveRoot = Path.Combine(TestRootPath, "slave-delete");
        Directory.CreateDirectory(masterRoot);
        Directory.CreateDirectory(slaveRoot);

        var staleFilePath = Path.Combine(slaveRoot, "stale.txt");
        await File.WriteAllTextAsync(staleFilePath, "stale");

        await WriteLocalLocalConfigAsync(masterRoot, slaveRoot, syncMode: "PushAndDelete");

        using var host = await CreateHostAsync(TestRootPath);
        await WaitForNodesAsync(host.Services, "local-master", "local-slave");
        var planManager = host.Services.GetRequiredService<ISyncPlanManager>();

        var results = await planManager.ExecutePlanNowAsync("local-filesystem-test", CancellationToken.None);
        TestAssert.ContainsOnlyExpectedResults(results, SyncTaskResult.Success, SyncTaskResult.NoChanges);

        Assert.False(File.Exists(staleFilePath));

        await host.StopAsync();
    }

    [Fact]
    [Trait("Category", "Offline")]
    public async Task LocalFilesystemPlan_Should_SyncSameSizeContentUpdate()
    {
        var masterRoot = Path.Combine(TestRootPath, "master-content-update");
        var slaveRoot = Path.Combine(TestRootPath, "slave-content-update");
        Directory.CreateDirectory(masterRoot);
        Directory.CreateDirectory(slaveRoot);

        var masterFilePath = Path.Combine(masterRoot, "note.txt");
        await File.WriteAllTextAsync(masterFilePath, "hello-1111");

        await WriteLocalLocalConfigAsync(masterRoot, slaveRoot);

        using var host = await CreateHostAsync(TestRootPath);
        await WaitForNodesAsync(host.Services, "local-master", "local-slave");
        var planManager = host.Services.GetRequiredService<ISyncPlanManager>();

        await planManager.ExecutePlanNowAsync("local-filesystem-test", CancellationToken.None);

        await Task.Delay(1100);
        await File.WriteAllTextAsync(masterFilePath, "hello-2222");

        var results = await planManager.ExecutePlanNowAsync("local-filesystem-test", CancellationToken.None);
        Assert.NotEmpty(results);

        var slaveFilePath = Path.Combine(slaveRoot, "note.txt");
        Assert.True(File.Exists(slaveFilePath));
        Assert.Equal("hello-2222", await File.ReadAllTextAsync(slaveFilePath));

        await host.StopAsync();
    }

    [Fact]
    [Trait("Category", "Offline")]
    public async Task LocalFilesystemPlan_Should_DeleteOtherSideFile_InBidirectionalMode()
    {
        var masterRoot = Path.Combine(TestRootPath, "master-bidirectional-delete");
        var slaveRoot = Path.Combine(TestRootPath, "slave-bidirectional-delete");
        Directory.CreateDirectory(masterRoot);
        Directory.CreateDirectory(slaveRoot);

        var masterFilePath = Path.Combine(masterRoot, "obsolete.txt");
        await File.WriteAllTextAsync(masterFilePath, "remove-me");

        await WriteLocalLocalConfigAsync(masterRoot, slaveRoot);

        using var host = await CreateHostAsync(TestRootPath);
        await WaitForNodesAsync(host.Services, "local-master", "local-slave");
        var planManager = host.Services.GetRequiredService<ISyncPlanManager>();

        // 建立基线，确保历史中已经记录双方都存在该文件。
        await planManager.ExecutePlanNowAsync("local-filesystem-test", CancellationToken.None);
        Assert.True(File.Exists(Path.Combine(slaveRoot, "obsolete.txt")));

        File.Delete(masterFilePath);

        var results = await planManager.ExecutePlanNowAsync("local-filesystem-test", CancellationToken.None);
        Assert.NotEmpty(results);
        Assert.False(File.Exists(Path.Combine(slaveRoot, "obsolete.txt")));

        await host.StopAsync();
    }

    [Fact]
    [Trait("Category", "Offline")]
    public async Task LocalFilesystemPlan_Should_KeepNewerMasterVersion_WhenConflictStrategyIsKeepNewer()
    {
        var masterRoot = Path.Combine(TestRootPath, "master-keep-newer-master");
        var slaveRoot = Path.Combine(TestRootPath, "slave-keep-newer-master");
        Directory.CreateDirectory(masterRoot);
        Directory.CreateDirectory(slaveRoot);

        var masterFilePath = Path.Combine(masterRoot, "conflict.txt");
        var slaveFilePath = Path.Combine(slaveRoot, "conflict.txt");
        await File.WriteAllTextAsync(masterFilePath, "base-version");

        await WriteLocalLocalConfigAsync(masterRoot, slaveRoot, conflictResolutionStrategy: "KeepNewer");

        using var host = await CreateHostAsync(TestRootPath);
        await WaitForNodesAsync(host.Services, "local-master", "local-slave");
        var planManager = host.Services.GetRequiredService<ISyncPlanManager>();
        Assert.Equal(ConflictResolutionStrategy.KeepNewer, planManager.GetPlanById("local-filesystem-test")!.SlaveConfigurations[0].ConflictResolutionStrategy);

        await planManager.ExecutePlanNowAsync("local-filesystem-test", CancellationToken.None);

        await Task.Delay(1100);
        await File.WriteAllTextAsync(slaveFilePath, "slave-version");
        await Task.Delay(1100);
        await File.WriteAllTextAsync(masterFilePath, "master-version");

        var results = await planManager.ExecutePlanNowAsync("local-filesystem-test", CancellationToken.None);
        Assert.NotEmpty(results);
        Assert.DoesNotContain(SyncTaskResult.Conflict, results.Values);
        Assert.Equal("master-version", await File.ReadAllTextAsync(masterFilePath));
        Assert.Equal("master-version", await File.ReadAllTextAsync(slaveFilePath));

        await host.StopAsync();
    }

    [Fact]
    [Trait("Category", "Offline")]
    public async Task LocalFilesystemPlan_Should_KeepNewerSlaveVersion_WhenConflictStrategyIsKeepNewer()
    {
        var masterRoot = Path.Combine(TestRootPath, "master-keep-newer-slave");
        var slaveRoot = Path.Combine(TestRootPath, "slave-keep-newer-slave");
        Directory.CreateDirectory(masterRoot);
        Directory.CreateDirectory(slaveRoot);

        var masterFilePath = Path.Combine(masterRoot, "conflict.txt");
        var slaveFilePath = Path.Combine(slaveRoot, "conflict.txt");
        await File.WriteAllTextAsync(masterFilePath, "base-version");

        await WriteLocalLocalConfigAsync(masterRoot, slaveRoot, conflictResolutionStrategy: "KeepNewer");

        using var host = await CreateHostAsync(TestRootPath);
        await WaitForNodesAsync(host.Services, "local-master", "local-slave");
        var planManager = host.Services.GetRequiredService<ISyncPlanManager>();
        Assert.Equal(ConflictResolutionStrategy.KeepNewer, planManager.GetPlanById("local-filesystem-test")!.SlaveConfigurations[0].ConflictResolutionStrategy);

        await planManager.ExecutePlanNowAsync("local-filesystem-test", CancellationToken.None);

        await Task.Delay(1100);
        await File.WriteAllTextAsync(masterFilePath, "master-version");
        await Task.Delay(1100);
        await File.WriteAllTextAsync(slaveFilePath, "slave-version");

        var results = await planManager.ExecutePlanNowAsync("local-filesystem-test", CancellationToken.None);
        Assert.NotEmpty(results);
        Assert.DoesNotContain(SyncTaskResult.Conflict, results.Values);
        Assert.Equal("slave-version", await File.ReadAllTextAsync(masterFilePath));
        Assert.Equal("slave-version", await File.ReadAllTextAsync(slaveFilePath));

        await host.StopAsync();
    }

    [Fact]
    [Trait("Category", "Offline")]
    public async Task LocalFilesystemPlan_Should_KeepMasterVersion_WhenConflictStrategyIsKeepLocal()
    {
        var masterRoot = Path.Combine(TestRootPath, "master-keep-local");
        var slaveRoot = Path.Combine(TestRootPath, "slave-keep-local");
        Directory.CreateDirectory(masterRoot);
        Directory.CreateDirectory(slaveRoot);

        var masterFilePath = Path.Combine(masterRoot, "conflict.txt");
        var slaveFilePath = Path.Combine(slaveRoot, "conflict.txt");
        await File.WriteAllTextAsync(masterFilePath, "base-version");

        await WriteLocalLocalConfigAsync(masterRoot, slaveRoot, conflictResolutionStrategy: "KeepLocal");

        using var host = await CreateHostAsync(TestRootPath);
        await WaitForNodesAsync(host.Services, "local-master", "local-slave");
        var planManager = host.Services.GetRequiredService<ISyncPlanManager>();

        await planManager.ExecutePlanNowAsync("local-filesystem-test", CancellationToken.None);

        await Task.Delay(1100);
        await File.WriteAllTextAsync(masterFilePath, "master-version");
        await Task.Delay(1100);
        await File.WriteAllTextAsync(slaveFilePath, "slave-version");

        var results = await planManager.ExecutePlanNowAsync("local-filesystem-test", CancellationToken.None);
        Assert.NotEmpty(results);
        Assert.DoesNotContain(SyncTaskResult.Conflict, results.Values);
        Assert.Equal("master-version", await File.ReadAllTextAsync(masterFilePath));
        Assert.Equal("master-version", await File.ReadAllTextAsync(slaveFilePath));

        await host.StopAsync();
    }

    [Fact]
    [Trait("Category", "Offline")]
    public async Task LocalFilesystemPlan_Should_KeepSlaveVersion_WhenConflictStrategyIsKeepRemote()
    {
        var masterRoot = Path.Combine(TestRootPath, "master-keep-remote");
        var slaveRoot = Path.Combine(TestRootPath, "slave-keep-remote");
        Directory.CreateDirectory(masterRoot);
        Directory.CreateDirectory(slaveRoot);

        var masterFilePath = Path.Combine(masterRoot, "conflict.txt");
        var slaveFilePath = Path.Combine(slaveRoot, "conflict.txt");
        await File.WriteAllTextAsync(masterFilePath, "base-version");

        await WriteLocalLocalConfigAsync(masterRoot, slaveRoot, conflictResolutionStrategy: "KeepRemote");

        using var host = await CreateHostAsync(TestRootPath);
        await WaitForNodesAsync(host.Services, "local-master", "local-slave");
        var planManager = host.Services.GetRequiredService<ISyncPlanManager>();

        await planManager.ExecutePlanNowAsync("local-filesystem-test", CancellationToken.None);

        await Task.Delay(1100);
        await File.WriteAllTextAsync(masterFilePath, "master-version");
        await Task.Delay(1100);
        await File.WriteAllTextAsync(slaveFilePath, "slave-version");

        var results = await planManager.ExecutePlanNowAsync("local-filesystem-test", CancellationToken.None);
        Assert.NotEmpty(results);
        Assert.DoesNotContain(SyncTaskResult.Conflict, results.Values);
        Assert.Equal("slave-version", await File.ReadAllTextAsync(masterFilePath));
        Assert.Equal("slave-version", await File.ReadAllTextAsync(slaveFilePath));

        await host.StopAsync();
    }

    [Fact]
    [Trait("Category", "Offline")]
    public async Task ImplicitHostLocalPlan_Should_IgnoreInternalHistoryDatabaseFiles()
    {
        var hostWorkspaceRoot = TestRootPath;
        var slaveRoot = Path.Combine(Path.GetTempPath(), "UniversalSyncService-HostLocalSlave", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(slaveRoot);
        Directory.CreateDirectory(Path.Combine(hostWorkspaceRoot, "data"));

        var masterFilePath = Path.Combine(hostWorkspaceRoot, "host-file.txt");
        await File.WriteAllTextAsync(masterFilePath, "from-host-local");
        await File.WriteAllTextAsync(Path.Combine(hostWorkspaceRoot, "data", "sync-history.db-wal"), "history-wal");
        await File.WriteAllTextAsync(Path.Combine(hostWorkspaceRoot, "data", "sync-history.db-shm"), "history-shm");
        await File.WriteAllTextAsync(Path.Combine(hostWorkspaceRoot, "data", "sync-history.db-journal"), "history-journal");

        await WriteHostLocalConfigAsync(
            slaveRoot,
            hostWorkspacePath: ".",
            planId: "host-local-filesystem-test",
            planName: "宿主本地节点测试计划",
            planDescription: "用于验证宿主历史数据库不会被作为同步对象处理。",
            targetPath: ".");

        using var host = await CreateHostAsync(TestRootPath);
        await WaitForNodesAsync(host.Services, "host-local", "local-slave");
        var planManager = host.Services.GetRequiredService<ISyncPlanManager>();

        var results = await planManager.ExecutePlanNowAsync("host-local-filesystem-test", CancellationToken.None);
        Assert.NotEmpty(results);
        Assert.DoesNotContain(SyncTaskResult.Failed, results.Values);

        Assert.False(File.Exists(Path.Combine(slaveRoot, "data", "sync-history.db")));
        Assert.False(File.Exists(Path.Combine(slaveRoot, "data", "sync-history.db-wal")));
        Assert.False(File.Exists(Path.Combine(slaveRoot, "data", "sync-history.db-shm")));
        Assert.False(File.Exists(Path.Combine(slaveRoot, "data", "sync-history.db-journal")));

        await host.StopAsync();
        Directory.Delete(slaveRoot, recursive: true);
    }

    [Fact]
    [Trait("Category", "Offline")]
    public async Task HostLocalPlan_Should_AllowAbsoluteTargetPath()
    {
        var slaveRoot = Path.Combine(TestRootPath, "slave-absolute-target");
        var masterAbsoluteRoot = Path.Combine(TestRootPath, "absolute-host-target");
        Directory.CreateDirectory(slaveRoot);
        Directory.CreateDirectory(masterAbsoluteRoot);

        var slaveFilePath = Path.Combine(slaveRoot, "note.txt");
        await File.WriteAllTextAsync(slaveFilePath, "absolute-host-target-data");

        await WriteHostLocalConfigAsync(
            slaveRoot,
            hostWorkspacePath: "sync-test/master",
            planId: "host-local-absolute-target-test",
            planName: "宿主绝对路径计划",
            planDescription: "用于验证 host-local 可显式使用绝对目标路径。",
            targetPath: masterAbsoluteRoot);

        using var host = await CreateHostAsync(TestRootPath);
        await WaitForNodesAsync(host.Services, "host-local", "local-slave");
        var planManager = host.Services.GetRequiredService<ISyncPlanManager>();

        var results = await planManager.ExecutePlanNowAsync("host-local-absolute-target-test", CancellationToken.None);
        Assert.NotEmpty(results);
        Assert.DoesNotContain(SyncTaskResult.Failed, results.Values);
        Assert.True(File.Exists(Path.Combine(masterAbsoluteRoot, "note.txt")));
        Assert.Equal("absolute-host-target-data", await File.ReadAllTextAsync(Path.Combine(masterAbsoluteRoot, "note.txt")));

        await host.StopAsync();
    }

    [Fact]
    [Trait("Category", "Offline")]
    public async Task LocalFilesystemPlan_Should_AllowAbsoluteTargetPath_ForRegularLocalMaster()
    {
        var masterRoot = Path.Combine(TestRootPath, "master-regular-absolute");
        var slaveRoot = Path.Combine(TestRootPath, "slave-regular-absolute");
        var absoluteTarget = Path.Combine(TestRootPath, "absolute-target");
        Directory.CreateDirectory(masterRoot);
        Directory.CreateDirectory(slaveRoot);
        Directory.CreateDirectory(absoluteTarget);

        await File.WriteAllTextAsync(Path.Combine(slaveRoot, "local-master-absolute.txt"), "from-local-slave");

        await WriteLocalLocalConfigAsync(masterRoot, slaveRoot, targetPath: absoluteTarget);

        using var host = await CreateHostAsync(TestRootPath);
        await WaitForNodesAsync(host.Services, "local-master", "local-slave");
        var planManager = host.Services.GetRequiredService<ISyncPlanManager>();

        var results = await planManager.ExecutePlanNowAsync("local-filesystem-test", CancellationToken.None);
        Assert.NotEmpty(results);
        Assert.DoesNotContain(SyncTaskResult.Failed, results.Values);
        Assert.True(File.Exists(Path.Combine(absoluteTarget, "local-master-absolute.txt")));
        Assert.Equal("from-local-slave", await File.ReadAllTextAsync(Path.Combine(absoluteTarget, "local-master-absolute.txt")));

        await host.StopAsync();
    }

    [Fact]
    [Trait("Category", "Offline")]
    public async Task LocalFilesystemPlan_Should_AllowAbsoluteSourcePath_ForRegularLocalSlave()
    {
        var masterRoot = Path.Combine(TestRootPath, "master-local-absolute-source");
        var slaveRoot = Path.Combine(TestRootPath, "slave-local-absolute-source");
        var absoluteSlaveSource = Path.Combine(TestRootPath, "absolute-slave-source");
        Directory.CreateDirectory(masterRoot);
        Directory.CreateDirectory(slaveRoot);
        Directory.CreateDirectory(absoluteSlaveSource);

        await File.WriteAllTextAsync(Path.Combine(absoluteSlaveSource, "local-slave-absolute.txt"), "from-local-absolute-source");

        await WriteLocalLocalConfigAsync(masterRoot, slaveRoot, sourcePath: absoluteSlaveSource);

        using var host = await CreateHostAsync(TestRootPath);
        await WaitForNodesAsync(host.Services, "local-master", "local-slave");
        var planManager = host.Services.GetRequiredService<ISyncPlanManager>();

        var results = await planManager.ExecutePlanNowAsync("local-filesystem-test", CancellationToken.None);
        Assert.NotEmpty(results);
        Assert.DoesNotContain(SyncTaskResult.Failed, results.Values);
        Assert.True(File.Exists(Path.Combine(masterRoot, "local-slave-absolute.txt")));
        Assert.Equal("from-local-absolute-source", await File.ReadAllTextAsync(Path.Combine(masterRoot, "local-slave-absolute.txt")));

        await host.StopAsync();
    }

    [Fact]
    [Trait("Category", "AuthNegative")]
    public async Task OneDriveSlavePlan_Should_RejectAbsoluteSourcePath_ForNonLocalNode()
    {
        var masterRoot = Path.Combine(TestRootPath, "master-onedrive-absolute-reject");
        var forbiddenAbsoluteSource = Path.Combine(TestRootPath, "forbidden-onedrive-source");
        Directory.CreateDirectory(masterRoot);
        Directory.CreateDirectory(forbiddenAbsoluteSource);

        await WriteOneDriveAuthNegativeConfigAsync(masterRoot, forbiddenAbsoluteSource);

        using var host = await CreateHostAsync(TestRootPath);
        await WaitForNodesAsync(host.Services, "local-master", "one-drive-slave");
        var planManager = host.Services.GetRequiredService<ISyncPlanManager>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => planManager.ExecutePlanNowAsync("local-filesystem-test", CancellationToken.None));
        Assert.Contains("从节点路径", exception.Message);
        Assert.Contains("本地节点（Local/host-local）", exception.Message);

        await host.StopAsync();
    }

    private async Task InitializeCoreAsync()
    {
        _testRoot = await TempContentRoot.CreateAsync("UniversalSyncService-Integration");
    }

    private static async Task<IHost> CreateHostAsync(string contentRoot)
    {
        var settings = new Dictionary<string, string?>
        {
            [HostDefaults.ContentRootKey] = contentRoot,
            [HostDefaults.EnvironmentKey] = Environments.Production
        };

        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = contentRoot,
            EnvironmentName = Environments.Production,
            Configuration = new ConfigurationManager()
        });

        builder.Configuration.AddInMemoryCollection(settings);
        builder.ConfigureUniversalSyncConfiguration(Array.Empty<string>());
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

    private async Task WriteLocalLocalConfigAsync(
        string masterRoot,
        string slaveRoot,
        string syncMode = "Bidirectional",
        string conflictResolutionStrategy = "Manual",
        string sourcePath = ".",
        string targetPath = ".")
    {
        await ConfigLoader.WriteYamlAsync(
            outputDirectory: TestRootPath,
            templatePath: TestConfigPaths.GetTemplatePath("integration.test.yaml"),
            localOverridePath: TestConfigPaths.GetLocalOverridePath("integration.test.yaml"),
            placeholders: new Dictionary<string, string>
            {
                ["MASTER_ROOT"] = masterRoot.Replace("\\", "/"),
                ["SLAVE_ROOT"] = slaveRoot.Replace("\\", "/"),
                ["SYNC_MODE"] = syncMode,
                ["CONFLICT_RESOLUTION_STRATEGY"] = conflictResolutionStrategy,
                ["SOURCE_PATH"] = sourcePath.Replace("\\", "/"),
                ["TARGET_PATH"] = targetPath.Replace("\\", "/")
            });
    }

    private async Task WriteHostLocalConfigAsync(
        string slaveRoot,
        string hostWorkspacePath,
        string planId,
        string planName,
        string planDescription,
        string targetPath)
    {
        await ConfigLoader.WriteYamlAsync(
            outputDirectory: TestRootPath,
            templatePath: TestConfigPaths.GetTemplatePath("integration.host-local.test.yaml"),
            localOverridePath: TestConfigPaths.GetLocalOverridePath("integration.host-local.test.yaml"),
            placeholders: new Dictionary<string, string>
            {
                ["SLAVE_ROOT"] = slaveRoot.Replace("\\", "/"),
                ["HOST_WORKSPACE_PATH"] = hostWorkspacePath,
                ["PLAN_ID"] = planId,
                ["PLAN_NAME"] = planName,
                ["PLAN_DESCRIPTION"] = planDescription,
                ["TARGET_PATH"] = targetPath.Replace("\\", "/")
            });
    }

    private async Task WriteOneDriveAuthNegativeConfigAsync(string masterRoot, string absoluteSourcePath)
    {
        var authNegativeSlaveRoot = Path.Combine(TestRootPath, "slave-onedrive-absolute-reject");
        Directory.CreateDirectory(authNegativeSlaveRoot);

        await ConfigLoader.WriteYamlAsync(
            outputDirectory: TestRootPath,
            templatePath: TestConfigPaths.GetTemplatePath("integration.authnegative.onedrive.test.yaml"),
            localOverridePath: TestConfigPaths.GetLocalOverridePath("integration.authnegative.onedrive.test.yaml"),
            placeholders: new Dictionary<string, string>
            {
                ["MASTER_ROOT"] = masterRoot.Replace("\\", "/"),
                ["SLAVE_ROOT"] = authNegativeSlaveRoot.Replace("\\", "/"),
                ["SYNC_MODE"] = "Bidirectional",
                ["CONFLICT_RESOLUTION_STRATEGY"] = "Manual",
                ["SOURCE_PATH"] = absoluteSourcePath.Replace("\\", "/"),
                ["TARGET_PATH"] = "."
            });
    }
}

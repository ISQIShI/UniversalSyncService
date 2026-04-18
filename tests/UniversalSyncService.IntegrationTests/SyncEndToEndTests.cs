using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using UniversalSyncService.Abstractions.SyncManagement;
using UniversalSyncService.Abstractions.SyncManagement.Engine;
using UniversalSyncService.Abstractions.SyncManagement.Tasks;
using UniversalSyncService.Core.DependencyInjection;
using UniversalSyncService.Core.SyncManagement.ConfigNodes;
using UniversalSyncService.Host.Configuration;
using Xunit;

namespace UniversalSyncService.IntegrationTests;

public sealed class SyncEndToEndTests : IAsyncLifetime
{
    private string _testRootPath = string.Empty;

    public Task InitializeAsync()
    {
        _testRootPath = Path.Combine(Path.GetTempPath(), "UniversalSyncService-Integration", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testRootPath);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (Directory.Exists(_testRootPath))
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    Directory.Delete(_testRootPath, recursive: true);
                    break;
                }
                catch (IOException) when (attempt < 4)
                {
                    await Task.Delay(200);
                }
                catch (UnauthorizedAccessException) when (attempt < 4)
                {
                    await Task.Delay(200);
                }
                catch (IOException)
                {
                    break;
                }
                catch (UnauthorizedAccessException)
                {
                    break;
                }
            }
        }
    }

    [Fact]
    public async Task LocalFilesystemPlan_Should_CopyFile_And_WriteSqliteHistory()
    {
        var masterRoot = Path.Combine(_testRootPath, "master");
        var slaveRoot = Path.Combine(_testRootPath, "slave");
        Directory.CreateDirectory(masterRoot);
        Directory.CreateDirectory(slaveRoot);

        var sourceFilePath = Path.Combine(masterRoot, "hello.txt");
        await File.WriteAllTextAsync(sourceFilePath, "hello-sync");

        var configPath = Path.Combine(_testRootPath, "appsettings.yaml");
        await File.WriteAllTextAsync(configPath, CreateTestYaml(masterRoot, slaveRoot));

        using var host = await CreateHostAsync(_testRootPath);
        await WaitForNodesAsync(host.Services, "local-master", "local-slave");
        var planManager = host.Services.GetRequiredService<ISyncPlanManager>();

        var plans = planManager.GetAllPlans();
        Assert.Single(plans);
        Assert.Equal("local-filesystem-test", plans[0].Id);

        var results = await planManager.ExecutePlanNowAsync("local-filesystem-test", CancellationToken.None);
        Assert.NotEmpty(results);
        Assert.All(results.Values, result => Assert.Contains(result, new[] { SyncTaskResult.Success, SyncTaskResult.NoChanges }));

        var targetFilePath = Path.Combine(slaveRoot, "hello.txt");
        Assert.True(File.Exists(targetFilePath));
        Assert.Equal("hello-sync", await File.ReadAllTextAsync(targetFilePath));

        var sqlitePath = Path.Combine(_testRootPath, "data", "sync-history.db");
        Assert.True(File.Exists(sqlitePath));

        var executedPlan = planManager.GetPlanById("local-filesystem-test");
        Assert.NotNull(executedPlan);
        Assert.True(executedPlan.ExecutionCount >= 1);

        await host.StopAsync();
    }

    [Fact]
    public async Task LocalFilesystemPlan_Should_SyncNestedDirectories()
    {
        var masterRoot = Path.Combine(_testRootPath, "master-update");
        var slaveRoot = Path.Combine(_testRootPath, "slave-update");
        Directory.CreateDirectory(Path.Combine(masterRoot, "docs", "nested"));
        Directory.CreateDirectory(slaveRoot);

        var masterFilePath = Path.Combine(masterRoot, "docs", "nested", "note.txt");
        var slaveFilePath = Path.Combine(slaveRoot, "docs", "nested", "note.txt");
        await File.WriteAllTextAsync(masterFilePath, "v1");

        var configPath = Path.Combine(_testRootPath, "appsettings.yaml");
        await File.WriteAllTextAsync(configPath, CreateTestYaml(masterRoot, slaveRoot));

        using var host = await CreateHostAsync(_testRootPath);
        await WaitForNodesAsync(host.Services, "local-master", "local-slave");
        var planManager = host.Services.GetRequiredService<ISyncPlanManager>();

        var results = await planManager.ExecutePlanNowAsync("local-filesystem-test", CancellationToken.None);
        Assert.NotEmpty(results);

        Assert.True(File.Exists(slaveFilePath));
        Assert.Equal("v1", await File.ReadAllTextAsync(slaveFilePath));

        await host.StopAsync();
    }

    [Fact]
    public async Task LocalFilesystemPlan_Should_ReturnConflict_WhenBothSidesChangeAfterHistoryCreated()
    {
        var masterRoot = Path.Combine(_testRootPath, "master-conflict");
        var slaveRoot = Path.Combine(_testRootPath, "slave-conflict");
        Directory.CreateDirectory(masterRoot);
        Directory.CreateDirectory(slaveRoot);

        var masterFilePath = Path.Combine(masterRoot, "conflict.txt");
        var slaveFilePath = Path.Combine(slaveRoot, "conflict.txt");
        await File.WriteAllTextAsync(masterFilePath, "base-version");

        var configPath = Path.Combine(_testRootPath, "appsettings.yaml");
        await File.WriteAllTextAsync(configPath, CreateTestYaml(masterRoot, slaveRoot));

        using var host = await CreateHostAsync(_testRootPath);
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
    public async Task LocalFilesystemPlan_Should_DeleteRemoteFile_InPushAndDeleteMode()
    {
        var masterRoot = Path.Combine(_testRootPath, "master-delete");
        var slaveRoot = Path.Combine(_testRootPath, "slave-delete");
        Directory.CreateDirectory(masterRoot);
        Directory.CreateDirectory(slaveRoot);

        var staleFilePath = Path.Combine(slaveRoot, "stale.txt");
        await File.WriteAllTextAsync(staleFilePath, "stale");

        var configPath = Path.Combine(_testRootPath, "appsettings.yaml");
        await File.WriteAllTextAsync(configPath, CreateTestYaml(masterRoot, slaveRoot, "PushAndDelete"));

        using var host = await CreateHostAsync(_testRootPath);
        await WaitForNodesAsync(host.Services, "local-master", "local-slave");
        var planManager = host.Services.GetRequiredService<ISyncPlanManager>();

        var results = await planManager.ExecutePlanNowAsync("local-filesystem-test", CancellationToken.None);
        Assert.NotEmpty(results);
        Assert.All(results.Values, result => Assert.Contains(result, new[] { SyncTaskResult.Success, SyncTaskResult.NoChanges }));

        Assert.False(File.Exists(staleFilePath));

        await host.StopAsync();
    }

    [Fact]
    public async Task LocalFilesystemPlan_Should_SyncSameSizeContentUpdate()
    {
        var masterRoot = Path.Combine(_testRootPath, "master-content-update");
        var slaveRoot = Path.Combine(_testRootPath, "slave-content-update");
        Directory.CreateDirectory(masterRoot);
        Directory.CreateDirectory(slaveRoot);

        var masterFilePath = Path.Combine(masterRoot, "note.txt");
        await File.WriteAllTextAsync(masterFilePath, "hello-1111");

        var configPath = Path.Combine(_testRootPath, "appsettings.yaml");
        await File.WriteAllTextAsync(configPath, CreateTestYaml(masterRoot, slaveRoot));

        using var host = await CreateHostAsync(_testRootPath);
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
    public async Task LocalFilesystemPlan_Should_DeleteOtherSideFile_InBidirectionalMode()
    {
        var masterRoot = Path.Combine(_testRootPath, "master-bidirectional-delete");
        var slaveRoot = Path.Combine(_testRootPath, "slave-bidirectional-delete");
        Directory.CreateDirectory(masterRoot);
        Directory.CreateDirectory(slaveRoot);

        var masterFilePath = Path.Combine(masterRoot, "obsolete.txt");
        await File.WriteAllTextAsync(masterFilePath, "remove-me");

        var configPath = Path.Combine(_testRootPath, "appsettings.yaml");
        await File.WriteAllTextAsync(configPath, CreateTestYaml(masterRoot, slaveRoot));

        using var host = await CreateHostAsync(_testRootPath);
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
    public async Task LocalFilesystemPlan_Should_KeepNewerMasterVersion_WhenConflictStrategyIsKeepNewer()
    {
        var masterRoot = Path.Combine(_testRootPath, "master-keep-newer-master");
        var slaveRoot = Path.Combine(_testRootPath, "slave-keep-newer-master");
        Directory.CreateDirectory(masterRoot);
        Directory.CreateDirectory(slaveRoot);

        var masterFilePath = Path.Combine(masterRoot, "conflict.txt");
        var slaveFilePath = Path.Combine(slaveRoot, "conflict.txt");
        await File.WriteAllTextAsync(masterFilePath, "base-version");

        var configPath = Path.Combine(_testRootPath, "appsettings.yaml");
        await File.WriteAllTextAsync(configPath, CreateTestYaml(masterRoot, slaveRoot, conflictResolutionStrategy: "KeepNewer"));

        using var host = await CreateHostAsync(_testRootPath);
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
    public async Task LocalFilesystemPlan_Should_KeepNewerSlaveVersion_WhenConflictStrategyIsKeepNewer()
    {
        var masterRoot = Path.Combine(_testRootPath, "master-keep-newer-slave");
        var slaveRoot = Path.Combine(_testRootPath, "slave-keep-newer-slave");
        Directory.CreateDirectory(masterRoot);
        Directory.CreateDirectory(slaveRoot);

        var masterFilePath = Path.Combine(masterRoot, "conflict.txt");
        var slaveFilePath = Path.Combine(slaveRoot, "conflict.txt");
        await File.WriteAllTextAsync(masterFilePath, "base-version");

        var configPath = Path.Combine(_testRootPath, "appsettings.yaml");
        await File.WriteAllTextAsync(configPath, CreateTestYaml(masterRoot, slaveRoot, conflictResolutionStrategy: "KeepNewer"));

        using var host = await CreateHostAsync(_testRootPath);
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
    public async Task LocalFilesystemPlan_Should_KeepMasterVersion_WhenConflictStrategyIsKeepLocal()
    {
        var masterRoot = Path.Combine(_testRootPath, "master-keep-local");
        var slaveRoot = Path.Combine(_testRootPath, "slave-keep-local");
        Directory.CreateDirectory(masterRoot);
        Directory.CreateDirectory(slaveRoot);

        var masterFilePath = Path.Combine(masterRoot, "conflict.txt");
        var slaveFilePath = Path.Combine(slaveRoot, "conflict.txt");
        await File.WriteAllTextAsync(masterFilePath, "base-version");

        var configPath = Path.Combine(_testRootPath, "appsettings.yaml");
        await File.WriteAllTextAsync(configPath, CreateTestYaml(masterRoot, slaveRoot, conflictResolutionStrategy: "KeepLocal"));

        using var host = await CreateHostAsync(_testRootPath);
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
    public async Task LocalFilesystemPlan_Should_KeepSlaveVersion_WhenConflictStrategyIsKeepRemote()
    {
        var masterRoot = Path.Combine(_testRootPath, "master-keep-remote");
        var slaveRoot = Path.Combine(_testRootPath, "slave-keep-remote");
        Directory.CreateDirectory(masterRoot);
        Directory.CreateDirectory(slaveRoot);

        var masterFilePath = Path.Combine(masterRoot, "conflict.txt");
        var slaveFilePath = Path.Combine(slaveRoot, "conflict.txt");
        await File.WriteAllTextAsync(masterFilePath, "base-version");

        var configPath = Path.Combine(_testRootPath, "appsettings.yaml");
        await File.WriteAllTextAsync(configPath, CreateTestYaml(masterRoot, slaveRoot, conflictResolutionStrategy: "KeepRemote"));

        using var host = await CreateHostAsync(_testRootPath);
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
    public async Task ImplicitHostLocalPlan_Should_IgnoreInternalHistoryDatabaseFiles()
    {
        var hostWorkspaceRoot = _testRootPath;
        var slaveRoot = Path.Combine(Path.GetTempPath(), "UniversalSyncService-HostLocalSlave", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(slaveRoot);

        var masterFilePath = Path.Combine(hostWorkspaceRoot, "host-file.txt");
        await File.WriteAllTextAsync(masterFilePath, "from-host-local");

        var configPath = Path.Combine(_testRootPath, "appsettings.yaml");
        await File.WriteAllTextAsync(configPath, CreateImplicitHostLocalYaml(slaveRoot));

        using var host = await CreateHostAsync(_testRootPath);
        await WaitForNodesAsync(host.Services, "host-local", "local-slave");
        var planManager = host.Services.GetRequiredService<ISyncPlanManager>();

        var results = await planManager.ExecutePlanNowAsync("host-local-filesystem-test", CancellationToken.None);
        Assert.NotEmpty(results);
        Assert.DoesNotContain(SyncTaskResult.Failed, results.Values);

        Assert.False(File.Exists(Path.Combine(slaveRoot, "data", "sync-history.db")));
        Assert.False(File.Exists(Path.Combine(slaveRoot, "data", "sync-history.db-wal")));

        await host.StopAsync();
        Directory.Delete(slaveRoot, recursive: true);
    }

    [Fact]
    public async Task HostLocalPlan_Should_AllowAbsoluteTargetPath()
    {
        var slaveRoot = Path.Combine(_testRootPath, "slave-absolute-target");
        var masterAbsoluteRoot = Path.Combine(_testRootPath, "absolute-host-target");
        Directory.CreateDirectory(slaveRoot);
        Directory.CreateDirectory(masterAbsoluteRoot);

        var slaveFilePath = Path.Combine(slaveRoot, "note.txt");
        await File.WriteAllTextAsync(slaveFilePath, "absolute-host-target-data");

        var configPath = Path.Combine(_testRootPath, "appsettings.yaml");
        await File.WriteAllTextAsync(configPath, CreateHostLocalAbsoluteTargetYaml(slaveRoot, masterAbsoluteRoot));

        using var host = await CreateHostAsync(_testRootPath);
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
    public async Task LocalFilesystemPlan_Should_AllowAbsoluteTargetPath_ForRegularLocalMaster()
    {
        var masterRoot = Path.Combine(_testRootPath, "master-regular-absolute");
        var slaveRoot = Path.Combine(_testRootPath, "slave-regular-absolute");
        var absoluteTarget = Path.Combine(_testRootPath, "absolute-target");
        Directory.CreateDirectory(masterRoot);
        Directory.CreateDirectory(slaveRoot);
        Directory.CreateDirectory(absoluteTarget);

        await File.WriteAllTextAsync(Path.Combine(slaveRoot, "local-master-absolute.txt"), "from-local-slave");

        var configPath = Path.Combine(_testRootPath, "appsettings.yaml");
        await File.WriteAllTextAsync(configPath, CreateRegularLocalAbsoluteTargetYaml(masterRoot, slaveRoot, absoluteTarget));

        using var host = await CreateHostAsync(_testRootPath);
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
    public async Task LocalFilesystemPlan_Should_AllowAbsoluteSourcePath_ForRegularLocalSlave()
    {
        var masterRoot = Path.Combine(_testRootPath, "master-local-absolute-source");
        var slaveRoot = Path.Combine(_testRootPath, "slave-local-absolute-source");
        var absoluteSlaveSource = Path.Combine(_testRootPath, "absolute-slave-source");
        Directory.CreateDirectory(masterRoot);
        Directory.CreateDirectory(slaveRoot);
        Directory.CreateDirectory(absoluteSlaveSource);

        await File.WriteAllTextAsync(Path.Combine(absoluteSlaveSource, "local-slave-absolute.txt"), "from-local-absolute-source");

        var configPath = Path.Combine(_testRootPath, "appsettings.yaml");
        await File.WriteAllTextAsync(configPath, CreateRegularLocalAbsoluteSourceYaml(masterRoot, slaveRoot, absoluteSlaveSource));

        using var host = await CreateHostAsync(_testRootPath);
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
    public async Task OneDriveSlavePlan_Should_RejectAbsoluteSourcePath_ForNonLocalNode()
    {
        var masterRoot = Path.Combine(_testRootPath, "master-onedrive-absolute-reject");
        var slaveRoot = Path.Combine(_testRootPath, "slave-onedrive-absolute-reject");
        var forbiddenAbsoluteSource = Path.Combine(_testRootPath, "forbidden-onedrive-source");
        Directory.CreateDirectory(masterRoot);
        Directory.CreateDirectory(slaveRoot);
        Directory.CreateDirectory(forbiddenAbsoluteSource);

        var configPath = Path.Combine(_testRootPath, "appsettings.yaml");
        await File.WriteAllTextAsync(configPath, CreateOneDriveSlaveAbsoluteSourceYaml(masterRoot, slaveRoot, forbiddenAbsoluteSource));

        using var host = await CreateHostAsync(_testRootPath);
        await WaitForNodesAsync(host.Services, "local-master", "one-drive-slave");
        var planManager = host.Services.GetRequiredService<ISyncPlanManager>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => planManager.ExecutePlanNowAsync("local-filesystem-test", CancellationToken.None));
        Assert.Contains("从节点路径", exception.Message);
        Assert.Contains("本地节点（Local/host-local）", exception.Message);

        await host.StopAsync();
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

    private static string CreateTestYaml(string masterRoot, string slaveRoot, string syncMode = "Bidirectional", string conflictResolutionStrategy = "Manual")
    {
        return $@"UniversalSyncService:
  Service:
    ServiceName: ""IntegrationTestHost""
    HeartbeatIntervalSeconds: 60
  Interface:
    EnableGrpc: true
    EnableHttpApi: true
    EnableWebConsole: true
    ManagementApiKey: ""test-key""
  Logging:
    MinimumLevel: ""Information""
    EnableConsoleSink: false
    EnableFileSink: false
    Overrides: {{}}
    File:
      Path: ""logs/test-.log""
      RollingInterval: ""Day""
      RetainedFileCountLimit: 2
      FileSizeLimitBytes: 1048576
      RollOnFileSizeLimit: true
      OutputTemplate: ""{{Message:lj}}{{NewLine}}{{Exception}}""
  Plugins:
    EnablePluginSystem: false
    PluginDirectory: ""plugins""
    Descriptors: []
  Sync:
    EnableSyncFramework: true
    SchedulerPollingIntervalSeconds: 60
    MaxConcurrentTasks: 1
    HistoryRetentionVersions: 20
    HistoryStorePath: ""data/sync-history.db""
    Nodes:
      - Id: ""local-master""
        Name: ""本地主节点""
        NodeType: ""Local""
        ConnectionSettings:
          RootPath: ""{masterRoot.Replace("\\", "/")}""
        CustomOptions: {{}}
        CreatedAt: ""2026-04-09T00:00:00+08:00""
        ModifiedAt: ""2026-04-09T00:00:00+08:00""
        IsEnabled: true
      - Id: ""local-slave""
        Name: ""本地从节点""
        NodeType: ""Local""
        ConnectionSettings:
          RootPath: ""{slaveRoot.Replace("\\", "/")}""
        CustomOptions: {{}}
        CreatedAt: ""2026-04-09T00:00:00+08:00""
        ModifiedAt: ""2026-04-09T00:00:00+08:00""
        IsEnabled: true
    Plans:
      - Id: ""local-filesystem-test""
        Name: ""本地文件系统测试计划""
        Description: ""用于本地节点与普通文件系统同步对象的功能测试。""
        MasterNodeId: ""local-master""
        SyncItemType: ""FileSystem""
        SlaveConfigurations:
          - SlaveNodeId: ""local-slave""
            SyncMode: ""{syncMode}""
            SourcePath: "".""
            TargetPath: "".""
            EnableDeletionProtection: true
            ConflictResolutionStrategy: ""{conflictResolutionStrategy}""
            Filters: []
            Exclusions: []
        Schedule:
          TriggerType: ""Manual""
          EnableFileSystemWatcher: false
        IsEnabled: false
        CreatedAt: ""2026-04-09T00:00:00+08:00""
        ModifiedAt: ""2026-04-09T00:00:00+08:00""
        ExecutionCount: 0
";
    }

    private static string CreateImplicitHostLocalYaml(string slaveRoot)
    {
        return $@"UniversalSyncService:
  Service:
    ServiceName: ""IntegrationTestHost""
    HeartbeatIntervalSeconds: 60
  Interface:
    EnableGrpc: true
    EnableHttpApi: true
    EnableWebConsole: true
    ManagementApiKey: ""test-key""
  Logging:
    MinimumLevel: ""Information""
    EnableConsoleSink: false
    EnableFileSink: false
    Overrides: {{}}
    File:
      Path: ""logs/test-.log""
      RollingInterval: ""Day""
      RetainedFileCountLimit: 2
      FileSizeLimitBytes: 1048576
      RollOnFileSizeLimit: true
      OutputTemplate: ""{{Message:lj}}{{NewLine}}{{Exception}}""
  Plugins:
    EnablePluginSystem: false
    PluginDirectory: ""plugins""
    Descriptors: []
  Sync:
    EnableSyncFramework: true
    SchedulerPollingIntervalSeconds: 60
    MaxConcurrentTasks: 1
    HistoryRetentionVersions: 20
    HistoryStorePath: ""data/sync-history.db""
    HostWorkspacePath: "".""
    Nodes:
      - Id: ""local-slave""
        Name: ""本地从节点""
        NodeType: ""Local""
        ConnectionSettings:
          RootPath: ""{slaveRoot.Replace("\\", "/")}""
        CustomOptions: {{}}
        CreatedAt: ""2026-04-09T00:00:00+08:00""
        ModifiedAt: ""2026-04-09T00:00:00+08:00""
        IsEnabled: true
    Plans:
      - Id: ""host-local-filesystem-test""
        Name: ""宿主本地节点测试计划""
        Description: ""用于验证宿主历史数据库不会被作为同步对象处理。""
        MasterNodeId: ""host-local""
        SyncItemType: ""FileSystem""
        SlaveConfigurations:
          - SlaveNodeId: ""local-slave""
            SyncMode: ""Bidirectional""
            SourcePath: "".""
            TargetPath: "".""
            EnableDeletionProtection: true
            ConflictResolutionStrategy: ""Manual""
            Filters: []
            Exclusions: []
        Schedule:
          TriggerType: ""Manual""
          EnableFileSystemWatcher: false
        IsEnabled: false
        CreatedAt: ""2026-04-09T00:00:00+08:00""
        ModifiedAt: ""2026-04-09T00:00:00+08:00""
        ExecutionCount: 0
";
    }

    private static string CreateHostLocalAbsoluteTargetYaml(string slaveRoot, string masterAbsoluteRoot)
    {
        return $@"UniversalSyncService:
  Service:
    ServiceName: ""IntegrationTestHost""
    HeartbeatIntervalSeconds: 60
  Interface:
    EnableGrpc: true
    EnableHttpApi: true
    EnableWebConsole: true
    ManagementApiKey: ""test-key""
  Logging:
    MinimumLevel: ""Information""
    EnableConsoleSink: false
    EnableFileSink: false
    Overrides: {{}}
    File:
      Path: ""logs/test-.log""
      RollingInterval: ""Day""
      RetainedFileCountLimit: 2
      FileSizeLimitBytes: 1048576
      RollOnFileSizeLimit: true
      OutputTemplate: ""{{Message:lj}}{{NewLine}}{{Exception}}""
  Plugins:
    EnablePluginSystem: false
    PluginDirectory: ""plugins""
    Descriptors: []
  Sync:
    EnableSyncFramework: true
    SchedulerPollingIntervalSeconds: 60
    MaxConcurrentTasks: 1
    HistoryRetentionVersions: 20
    HistoryStorePath: ""data/sync-history.db""
    HostWorkspacePath: ""sync-test/master""
    Nodes:
      - Id: ""local-slave""
        Name: ""本地从节点""
        NodeType: ""Local""
        ConnectionSettings:
          RootPath: ""{slaveRoot.Replace("\\", "/")}""
        CustomOptions: {{}}
        CreatedAt: ""2026-04-09T00:00:00+08:00""
        ModifiedAt: ""2026-04-09T00:00:00+08:00""
        IsEnabled: true
    Plans:
      - Id: ""host-local-absolute-target-test""
        Name: ""宿主绝对路径计划""
        Description: ""用于验证 host-local 可显式使用绝对目标路径。""
        MasterNodeId: ""host-local""
        SyncItemType: ""FileSystem""
        SlaveConfigurations:
          - SlaveNodeId: ""local-slave""
            SyncMode: ""Bidirectional""
            SourcePath: "".""
            TargetPath: ""{masterAbsoluteRoot.Replace("\\", "/")}""
            EnableDeletionProtection: true
            ConflictResolutionStrategy: ""Manual""
            Filters: []
            Exclusions: []
        Schedule:
          TriggerType: ""Manual""
          EnableFileSystemWatcher: false
        IsEnabled: false
        CreatedAt: ""2026-04-09T00:00:00+08:00""
        ModifiedAt: ""2026-04-09T00:00:00+08:00""
        ExecutionCount: 0
";
    }

    private static string CreateRegularLocalAbsoluteTargetYaml(string masterRoot, string slaveRoot, string absoluteTargetPath)
    {
        return CreateTestYaml(masterRoot, slaveRoot).Replace("TargetPath: \".\"", $"TargetPath: \"{absoluteTargetPath.Replace("\\", "/")}\"");
    }

    private static string CreateRegularLocalAbsoluteSourceYaml(string masterRoot, string slaveRoot, string absoluteSourcePath)
    {
        return CreateTestYaml(masterRoot, slaveRoot).Replace("SourcePath: \".\"", $"SourcePath: \"{absoluteSourcePath.Replace("\\", "/")}\"");
    }

    private static string CreateOneDriveSlaveAbsoluteSourceYaml(string masterRoot, string slaveRoot, string absoluteSourcePath)
    {
        return CreateTestYaml(masterRoot, slaveRoot)
            .Replace("Id: \"local-slave\"", "Id: \"one-drive-slave\"")
            .Replace("Name: \"本地从节点\"", "Name: \"OneDrive 从节点\"")
            .Replace("NodeType: \"Local\"", "NodeType: \"OneDrive\"")
            .Replace($"RootPath: \"{slaveRoot.Replace("\\", "/")}\"", "RootPath: \"Apps/UniversalSyncService\"")
            .Replace("SlaveNodeId: \"local-slave\"", "SlaveNodeId: \"one-drive-slave\"")
            .Replace("SourcePath: \".\"", $"SourcePath: \"{absoluteSourcePath.Replace("\\", "/")}\"");
    }
}

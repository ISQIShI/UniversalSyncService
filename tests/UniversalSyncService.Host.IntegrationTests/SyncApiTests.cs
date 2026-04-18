using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using UniversalSyncService.Host.Grpc;
using Xunit;

namespace UniversalSyncService.Host.IntegrationTests;

public sealed class SyncApiTests : IAsyncLifetime
{
    private string _contentRoot = string.Empty;

    public Task InitializeAsync()
    {
        _contentRoot = Path.Combine(Path.GetTempPath(), "UniversalSyncService-HostTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_contentRoot);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (Directory.Exists(_contentRoot))
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    Directory.Delete(_contentRoot, recursive: true);
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
                catch
                {
                    break;
                }
            }
        }
    }

    [Fact]
    public async Task ListPlans_Should_ReturnConfiguredPlan()
    {
        await File.WriteAllTextAsync(Path.Combine(_contentRoot, "appsettings.yaml"), CreateTestYaml(createSourceFile: false));

        await using var factory = CreateFactory();
        var client = CreateClient(factory);

        var response = await client.ListPlansAsync(new ListPlansRequest());

        Assert.Single(response.Plans);
        Assert.Equal("local-filesystem-test", response.Plans[0].Id);
        Assert.Equal("FileSystem", response.Plans[0].SyncItemType);
    }

    [Fact]
    public async Task ExecutePlanNow_Should_CopyFile_And_ReturnSummary()
    {
        await File.WriteAllTextAsync(Path.Combine(_contentRoot, "appsettings.yaml"), CreateTestYaml(createSourceFile: true));

        await using var factory = CreateFactory();
        var client = CreateClient(factory);

        var response = await client.ExecutePlanNowAsync(new ExecutePlanNowRequest
        {
            PlanId = "local-filesystem-test"
        });

        Assert.Equal("local-filesystem-test", response.PlanId);
        Assert.True(response.TotalTasks >= 1);

        var copiedFilePath = Path.Combine(_contentRoot, "slave", "api.txt");
        Assert.True(File.Exists(copiedFilePath));
        Assert.Equal("from-api", await File.ReadAllTextAsync(copiedFilePath));
    }

    [Fact]
    public async Task GetRecentHistory_Should_ReturnNewestEntries()
    {
        await File.WriteAllTextAsync(Path.Combine(_contentRoot, "appsettings.yaml"), CreateTestYaml(createSourceFile: true));

        await using var factory = CreateFactory();
        var client = CreateClient(factory);

        await client.ExecutePlanNowAsync(new ExecutePlanNowRequest
        {
            PlanId = "local-filesystem-test"
        });

        var response = await client.GetRecentHistoryAsync(new GetRecentHistoryRequest
        {
            PlanId = "local-filesystem-test",
            Limit = 1
        });

        Assert.Single(response.Entries);
        Assert.Equal("local-filesystem-test", response.Entries[0].PlanId);
    }

    [Fact]
    public async Task ExecutePlanNow_Should_ReturnNotFound_ForMissingPlan()
    {
        await File.WriteAllTextAsync(Path.Combine(_contentRoot, "appsettings.yaml"), CreateTestYaml(createSourceFile: false));

        await using var factory = CreateFactory();
        var client = CreateClient(factory);

        var exception = await Assert.ThrowsAsync<RpcException>(async () =>
        {
            await client.ExecutePlanNowAsync(new ExecutePlanNowRequest
            {
                PlanId = "missing-plan"
            });
        });

        Assert.Equal(StatusCode.NotFound, exception.StatusCode);
    }

    private TestWebApplicationFactory CreateFactory()
    {
        return new TestWebApplicationFactory(_contentRoot);
    }

    private static SyncApi.SyncApiClient CreateClient(WebApplicationFactory<Program> factory)
    {
        var httpClient = factory.CreateDefaultClient();
        var channel = GrpcChannel.ForAddress(httpClient.BaseAddress!, new GrpcChannelOptions
        {
            HttpClient = httpClient
        });

        return new SyncApi.SyncApiClient(channel);
    }

    private string CreateTestYaml(bool createSourceFile)
    {
        var masterRoot = Path.Combine(_contentRoot, "master");
        var slaveRoot = Path.Combine(_contentRoot, "slave");
        Directory.CreateDirectory(masterRoot);
        Directory.CreateDirectory(slaveRoot);

        if (createSourceFile)
        {
            File.WriteAllText(Path.Combine(masterRoot, "api.txt"), "from-api");
        }

        return $@"UniversalSyncService:
  Service:
    ServiceName: ""HostApiTest""
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
        Description: ""供 gRPC API 集成测试使用。""
        MasterNodeId: ""local-master""
        SyncItemType: ""FileSystem""
        SlaveConfigurations:
          - SlaveNodeId: ""local-slave""
            SyncMode: ""Bidirectional""
            SourcePath: "".""
            TargetPath: "".""
            EnableDeletionProtection: true
            Filters: []
            Exclusions: []
        Schedule:
          TriggerType: ""Manual""
          EnableFileSystemWatcher: false
        IsEnabled: true
        CreatedAt: ""2026-04-09T00:00:00+08:00""
        ModifiedAt: ""2026-04-09T00:00:00+08:00""
        ExecutionCount: 0
";
    }

    private sealed class TestWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly string _contentRoot;

        public TestWebApplicationFactory(string contentRoot)
        {
            _contentRoot = contentRoot;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseContentRoot(_contentRoot);
        }
    }
}

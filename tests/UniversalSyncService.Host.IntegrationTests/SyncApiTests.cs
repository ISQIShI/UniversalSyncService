using Grpc.Core;
using Microsoft.AspNetCore.Mvc.Testing;
using UniversalSyncService.Host.Grpc;
using UniversalSyncService.Testing;
using Xunit;

namespace UniversalSyncService.Host.IntegrationTests;

public sealed class SyncApiTests : IAsyncLifetime
{
    private TempContentRoot? _contentRoot;

    public Task InitializeAsync()
    {
        return InitializeCoreAsync();
    }

    public async Task DisposeAsync()
    {
        if (_contentRoot is not null)
        {
            await _contentRoot.DisposeAsync();
        }
    }

    [Fact]
    [Trait("Category", "Offline")]
    public async Task ListPlans_Should_ReturnConfiguredPlan()
    {
        await WriteHostConfigAsync(createSourceFile: false);

        await using var factory = CreateFactory();
        var client = CreateClient(factory);

        var response = await client.ListPlansAsync(new ListPlansRequest());

        Assert.Single(response.Plans);
        Assert.Equal("local-filesystem-test", response.Plans[0].Id);
        Assert.Equal("FileSystem", response.Plans[0].SyncItemType);
    }

    [Fact]
    [Trait("Category", "Offline")]
    public async Task ExecutePlanNow_Should_CopyFile_And_ReturnSummary()
    {
        await WriteHostConfigAsync(createSourceFile: true);

        await using var factory = CreateFactory();
        var client = CreateClient(factory);

        var response = await client.ExecutePlanNowAsync(new ExecutePlanNowRequest
        {
            PlanId = "local-filesystem-test"
        });

        Assert.Equal("local-filesystem-test", response.PlanId);
        Assert.True(response.TotalTasks >= 1);

        var copiedFilePath = Path.Combine(_contentRoot!.RootPath, "slave", "api.txt");
        Assert.True(File.Exists(copiedFilePath));
        Assert.Equal("from-api", await File.ReadAllTextAsync(copiedFilePath));
    }

    [Fact]
    [Trait("Category", "Offline")]
    public async Task GetRecentHistory_Should_ReturnNewestEntries()
    {
        await WriteHostConfigAsync(createSourceFile: true);

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
    [Trait("Category", "Offline")]
    public async Task ExecutePlanNow_Should_ReturnNotFound_ForMissingPlan()
    {
        await WriteHostConfigAsync(createSourceFile: false);

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

    private WebApplicationFactory<Program> CreateFactory()
    {
        return HostFactory.CreateHost(_contentRoot!.RootPath, enableWebRoot: false);
    }

    private static SyncApi.SyncApiClient CreateClient(WebApplicationFactory<Program> factory)
    {
        var channel = HostFactory.CreateGrpcChannel(factory);

        return new SyncApi.SyncApiClient(channel);
    }

    private async Task WriteHostConfigAsync(bool createSourceFile)
    {
        var masterRoot = Path.Combine(_contentRoot!.RootPath, "master");
        var slaveRoot = Path.Combine(_contentRoot!.RootPath, "slave");
        Directory.CreateDirectory(masterRoot);
        Directory.CreateDirectory(slaveRoot);

        if (createSourceFile)
        {
            File.WriteAllText(Path.Combine(masterRoot, "api.txt"), "from-api");
        }

        await ConfigLoader.WriteYamlAsync(
            outputDirectory: _contentRoot!.RootPath,
            templatePath: TestConfigPaths.GetTemplatePath("host.test.yaml"),
            localOverridePath: TestConfigPaths.GetLocalOverridePath("host.test.yaml"),
            placeholders: new Dictionary<string, string>
            {
                ["MASTER_ROOT"] = masterRoot.Replace("\\", "/"),
                ["SLAVE_ROOT"] = slaveRoot.Replace("\\", "/"),
                ["REQUIRE_API_KEY"] = "false"
            });
    }

    private async Task InitializeCoreAsync()
    {
        _contentRoot = await TempContentRoot.CreateAsync("UniversalSyncService-HostTests");
    }
}

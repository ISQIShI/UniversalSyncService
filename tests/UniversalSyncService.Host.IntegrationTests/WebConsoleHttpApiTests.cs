using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using UniversalSyncService.Testing;
using Xunit;

namespace UniversalSyncService.Host.IntegrationTests;

public sealed class WebConsoleHttpApiTests : IAsyncLifetime
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
    public async Task Health_Should_ReturnHealthy_WithoutAuth()
    {
        await WriteHostConfigAsync(createSourceFile: false);

        await using var factory = HostFactory.CreateHost(_contentRoot!.RootPath, enableWebRoot: true);
        var client = factory.CreateClient();

        var response = await client.GetFromJsonAsync<HealthResponse>("/health");
        Assert.NotNull(response);
        Assert.Equal("Healthy", response.Status);
    }

    [Fact]
    [Trait("Category", "Offline")]
    [Trait("Category", "AuthNegative")]
    public async Task PlansApi_Should_RequireApiKey()
    {
        await WriteHostConfigAsync(createSourceFile: false, requireApiKey: true);

        await using var factory = HostFactory.CreateHost(_contentRoot!.RootPath, enableWebRoot: true);
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/plans");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Offline")]
    public async Task PlansApi_Should_ReturnConfiguredPlan_WithApiKey()
    {
        await WriteHostConfigAsync(createSourceFile: false);

        await using var factory = HostFactory.CreateHost(_contentRoot!.RootPath, enableWebRoot: true);
        var client = CreateAuthorizedClient(factory);

        var response = await client.GetFromJsonAsync<List<PlanSummaryResponse>>("/api/v1/plans");
        Assert.NotNull(response);
        Assert.Single(response);
        Assert.Equal("local-filesystem-test", response[0].Id);
    }

    [Fact]
    [Trait("Category", "Offline")]
    public async Task PlansApi_Should_CreatePlan_WithApiKey()
    {
        await WriteHostConfigAsync(createSourceFile: false);

        await using var factory = HostFactory.CreateHost(_contentRoot!.RootPath, enableWebRoot: true);
        var client = CreateAuthorizedClient(factory);

        var request = new CreateOrUpdatePlanRequest(
            Name: "新增计划",
            Description: "通过 HTTP API 创建",
            MasterNodeId: "local-master",
            SyncItemType: "FileSystem",
            IsEnabled: true,
            TriggerType: "Manual",
            CronExpression: null,
            IntervalSeconds: null,
            EnableFileSystemWatcher: false,
            Slaves:
            [
                new PlanSlaveRequest(
                    SlaveNodeId: "local-slave",
                    SyncMode: "Bidirectional",
                    SourcePath: ".",
                    TargetPath: ".",
                    EnableDeletionProtection: true,
                    ConflictResolutionStrategy: null,
                    Filters: ["*.md"],
                    Exclusions: [".git/"])
            ]);

        var createResponse = await client.PostAsJsonAsync("/api/v1/plans", request);
        createResponse.EnsureSuccessStatusCode();

        var createdPlan = await createResponse.Content.ReadFromJsonAsync<PlanDetailResponse>();
        Assert.NotNull(createdPlan);
        Assert.Equal("新增计划", createdPlan.Name);
        Assert.Single(createdPlan.Slaves);
        Assert.Contains("*.md", createdPlan.Slaves[0].Filters);

        var plans = await client.GetFromJsonAsync<List<PlanSummaryResponse>>("/api/v1/plans");
        Assert.NotNull(plans);
        Assert.Equal(2, plans.Count);
    }

    [Fact]
    [Trait("Category", "Offline")]
    public async Task PlansApi_Should_UpdatePlan_WithApiKey()
    {
        await WriteHostConfigAsync(createSourceFile: false);

        await using var factory = HostFactory.CreateHost(_contentRoot!.RootPath, enableWebRoot: true);
        var client = CreateAuthorizedClient(factory);

        var request = new CreateOrUpdatePlanRequest(
            Name: "已更新计划",
            Description: "更新后的计划描述",
            MasterNodeId: "local-master",
            SyncItemType: "FileSystem",
            IsEnabled: false,
            TriggerType: "Scheduled",
            CronExpression: null,
            IntervalSeconds: 300,
            EnableFileSystemWatcher: false,
            Slaves:
            [
                new PlanSlaveRequest(
                    SlaveNodeId: "local-slave",
                    SyncMode: "Push",
                    SourcePath: "docs",
                    TargetPath: "backup",
                    EnableDeletionProtection: false,
                    ConflictResolutionStrategy: "KeepNewer",
                    Filters: ["*.txt", "*.md"],
                    Exclusions: ["bin/", "obj/"])
            ]);

        var updateResponse = await client.PutAsJsonAsync("/api/v1/plans/local-filesystem-test", request);
        updateResponse.EnsureSuccessStatusCode();

        var updatedPlan = await updateResponse.Content.ReadFromJsonAsync<PlanDetailResponse>();
        Assert.NotNull(updatedPlan);
        Assert.Equal("已更新计划", updatedPlan.Name);
        Assert.False(updatedPlan.IsEnabled);
        Assert.Equal("Scheduled", updatedPlan.TriggerType);
        Assert.Equal(300, updatedPlan.IntervalSeconds);
        Assert.Equal("Push", updatedPlan.Slaves[0].SyncMode);
        Assert.Equal("KeepNewer", updatedPlan.Slaves[0].ConflictResolutionStrategy);
        Assert.Contains("obj/", updatedPlan.Slaves[0].Exclusions);
    }

    [Fact]
    [Trait("Category", "Offline")]
    public async Task PlansApi_Should_DeletePlan_WithApiKey()
    {
        await WriteHostConfigAsync(createSourceFile: false);

        await using var factory = HostFactory.CreateHost(_contentRoot!.RootPath, enableWebRoot: true);
        var client = CreateAuthorizedClient(factory);

        var request = new CreateOrUpdatePlanRequest(
            Name: "待删除计划",
            Description: null,
            MasterNodeId: "local-master",
            SyncItemType: "FileSystem",
            IsEnabled: true,
            TriggerType: "Manual",
            CronExpression: null,
            IntervalSeconds: null,
            EnableFileSystemWatcher: false,
            Slaves:
            [
                new PlanSlaveRequest(
                    SlaveNodeId: "local-slave",
                    SyncMode: "Bidirectional",
                    SourcePath: ".",
                    TargetPath: ".",
                    EnableDeletionProtection: true,
                    ConflictResolutionStrategy: null,
                    Filters: [],
                    Exclusions: [])
            ]);

        var createResponse = await client.PostAsJsonAsync("/api/v1/plans", request);
        createResponse.EnsureSuccessStatusCode();
        var createdPlan = await createResponse.Content.ReadFromJsonAsync<PlanDetailResponse>();
        Assert.NotNull(createdPlan);

        var deleteResponse = await client.DeleteAsync($"/api/v1/plans/{createdPlan.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var detailResponse = await client.GetAsync($"/api/v1/plans/{createdPlan.Id}");
        Assert.Equal(HttpStatusCode.NotFound, detailResponse.StatusCode);
    }

    [Fact]
    [Trait("Category", "Offline")]
    public async Task ExecuteNowApi_Should_CopyFile_WithApiKey()
    {
        await WriteHostConfigAsync(createSourceFile: true);

        await using var factory = HostFactory.CreateHost(_contentRoot!.RootPath, enableWebRoot: true);
        var client = CreateAuthorizedClient(factory);

        var response = await client.PostAsync("/api/v1/plans/local-filesystem-test/execute-now", content: null);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<ExecutePlanResponse>();
        Assert.NotNull(payload);
        Assert.True(payload.TotalTasks >= 1);

        var copiedFilePath = Path.Combine(_contentRoot!.RootPath, "slave", "web.txt");
        Assert.True(File.Exists(copiedFilePath));
        Assert.Equal("from-http", await File.ReadAllTextAsync(copiedFilePath));
    }

    [Fact]
    [Trait("Category", "Offline")]
    public async Task GlobalHistoryApi_Should_ReturnEntries_AfterExecution()
    {
        await WriteHostConfigAsync(createSourceFile: true);

        await using var factory = HostFactory.CreateHost(_contentRoot!.RootPath, enableWebRoot: true);
        var client = CreateAuthorizedClient(factory);

        await client.PostAsync("/api/v1/plans/local-filesystem-test/execute-now", content: null);

        var response = await client.GetFromJsonAsync<List<HistoryEntryResponse>>("/api/v1/history?limit=5");
        Assert.NotNull(response);
        Assert.NotEmpty(response);
    }

    [Fact]
    [Trait("Category", "Offline")]
    public async Task ConfigSummaryApi_Should_ReturnCurrentPaths_AndCounts()
    {
        await WriteHostConfigAsync(createSourceFile: false);

        await using var factory = HostFactory.CreateHost(_contentRoot!.RootPath, enableWebRoot: true);
        var client = CreateAuthorizedClient(factory);

        var response = await client.GetFromJsonAsync<ConfigSummaryResponse>("/api/v1/config/summary");
        Assert.NotNull(response);
        Assert.True(response.EnableSyncFramework);
        Assert.Equal(2, response.NodeCount);
        Assert.Equal(1, response.PlanCount);
    }

    [Fact]
    [Trait("Category", "Offline")]
    public async Task NodesApi_Should_ExposeImplicitHostNode_AndSupportCrud()
    {
        await WriteHostConfigAsync(createSourceFile: false);

        await using var factory = HostFactory.CreateHost(_contentRoot!.RootPath, enableWebRoot: true);
        var client = CreateAuthorizedClient(factory);

        var initialNodes = await client.GetFromJsonAsync<List<NodeSummaryResponse>>("/api/v1/nodes");
        Assert.NotNull(initialNodes);
        Assert.Contains(initialNodes, node => node.Id == "host-local" && node.IsImplicitHostNode);
        Assert.Contains(initialNodes, node => node.Id == "local-slave" && Path.IsPathFullyQualified(node.RootPath ?? string.Empty));

        var createRequest = new CreateOrUpdateNodeRequest(
            Id: "local-archive",
            Name: "归档节点",
            NodeType: "Local",
            IsEnabled: true,
            ConnectionSettings: new Dictionary<string, string>
            {
                ["RootPath"] = Path.Combine(_contentRoot!.RootPath, "archive").Replace("\\", "/")
            },
            CustomOptions: new Dictionary<string, string>
            {
                ["Color"] = "Blue"
            });

        var createResponse = await client.PostAsJsonAsync("/api/v1/nodes", createRequest);
        createResponse.EnsureSuccessStatusCode();
        var createdNode = await createResponse.Content.ReadFromJsonAsync<NodeDetailResponse>();
        Assert.NotNull(createdNode);
        Assert.Equal("local-archive", createdNode.Id);
        Assert.False(createdNode.IsImplicitHostNode);
        Assert.Equal("已配置节点", createdNode.SourceLabel);
        Assert.True(Path.IsPathFullyQualified(createdNode.RootPath ?? string.Empty));
        Assert.True(Path.IsPathFullyQualified(createdNode.ConnectionSettings["RootPath"]));

        var updateRequest = new CreateOrUpdateNodeRequest(
            Id: null,
            Name: "归档节点-已更新",
            NodeType: "Local",
            IsEnabled: false,
            ConnectionSettings: new Dictionary<string, string>
            {
                ["RootPath"] = Path.Combine(_contentRoot!.RootPath, "archive-updated").Replace("\\", "/")
            },
            CustomOptions: new Dictionary<string, string>
            {
                ["Tier"] = "Cold"
            });

        var updateResponse = await client.PutAsJsonAsync("/api/v1/nodes/local-archive", updateRequest);
        updateResponse.EnsureSuccessStatusCode();
        var updatedNode = await updateResponse.Content.ReadFromJsonAsync<NodeDetailResponse>();
        Assert.NotNull(updatedNode);
        Assert.Equal("归档节点-已更新", updatedNode.Name);
        Assert.False(updatedNode.IsEnabled);
        Assert.Equal("Cold", updatedNode.CustomOptions["Tier"]);
        Assert.True(Path.IsPathFullyQualified(updatedNode.RootPath ?? string.Empty));
        Assert.True(Path.IsPathFullyQualified(updatedNode.ConnectionSettings["RootPath"]));

        var deleteResponse = await client.DeleteAsync("/api/v1/nodes/local-archive");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var deletedNodeResponse = await client.GetAsync("/api/v1/nodes/local-archive");
        Assert.Equal(HttpStatusCode.NotFound, deletedNodeResponse.StatusCode);
    }

    [Fact]
    [Trait("Category", "Offline")]
    public async Task NodesApi_Should_RejectMismatchedOrImplicitNodeIdOnUpdate()
    {
        await WriteHostConfigAsync(createSourceFile: false);

        await using var factory = HostFactory.CreateHost(_contentRoot!.RootPath, enableWebRoot: true);
        var client = CreateAuthorizedClient(factory);

        var mismatchedRequest = new CreateOrUpdateNodeRequest(
            Id: "other-node",
            Name: "不匹配节点",
            NodeType: "Local",
            IsEnabled: true,
            ConnectionSettings: new Dictionary<string, string>
            {
                ["RootPath"] = Path.Combine(_contentRoot!.RootPath, "other").Replace("\\", "/")
            },
            CustomOptions: new Dictionary<string, string>());

        var mismatchResponse = await client.PutAsJsonAsync("/api/v1/nodes/local-master", mismatchedRequest);
        Assert.Equal(HttpStatusCode.BadRequest, mismatchResponse.StatusCode);

        var implicitRequest = new CreateOrUpdateNodeRequest(
            Id: "host-local",
            Name: "宿主节点",
            NodeType: "Local",
            IsEnabled: true,
            ConnectionSettings: new Dictionary<string, string>
            {
                ["RootPath"] = Path.Combine(_contentRoot!.RootPath, "host").Replace("\\", "/")
            },
            CustomOptions: new Dictionary<string, string>());

        var implicitResponse = await client.PutAsJsonAsync("/api/v1/nodes/local-master", implicitRequest);
        Assert.Equal(HttpStatusCode.BadRequest, implicitResponse.StatusCode);
    }

    private static HttpClient CreateAuthorizedClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-key");
        return client;
    }

    private async Task WriteHostConfigAsync(bool createSourceFile, bool requireApiKey = false)
    {
        var masterRoot = Path.Combine(_contentRoot!.RootPath, "master");
        var slaveRoot = Path.Combine(_contentRoot!.RootPath, "slave");
        Directory.CreateDirectory(masterRoot);
        Directory.CreateDirectory(slaveRoot);

        if (createSourceFile)
        {
            File.WriteAllText(Path.Combine(masterRoot, "web.txt"), "from-http");
        }

        await ConfigLoader.WriteYamlAsync(
            outputDirectory: _contentRoot!.RootPath,
            templatePath: TestConfigPaths.GetTemplatePath("host.test.yaml"),
            localOverridePath: TestConfigPaths.GetLocalOverridePath("host.test.yaml"),
            placeholders: new Dictionary<string, string>
            {
                ["MASTER_ROOT"] = masterRoot.Replace("\\", "/"),
                ["SLAVE_ROOT"] = slaveRoot.Replace("\\", "/"),
                ["REQUIRE_API_KEY"] = requireApiKey.ToString().ToLowerInvariant()
            });
    }

    private async Task InitializeCoreAsync()
    {
        _contentRoot = await TempContentRoot.CreateAsync("UniversalSyncService-HttpTests", copyBuiltWebAssets: true);
    }

    private sealed record HealthResponse(string Status);

    private sealed record NodeSummaryResponse(string Id, string Name, string NodeType, bool IsEnabled, bool IsImplicitHostNode, string? RootPath, string SourceLabel);

    private sealed record NodeDetailResponse(
        string Id,
        string Name,
        string NodeType,
        bool IsEnabled,
        bool IsImplicitHostNode,
        string? RootPath,
        string SourceLabel,
        IReadOnlyDictionary<string, string> ConnectionSettings,
        IReadOnlyDictionary<string, string> CustomOptions,
        DateTimeOffset CreatedAt,
        DateTimeOffset ModifiedAt);

    private sealed record CreateOrUpdateNodeRequest(
        string? Id,
        string Name,
        string NodeType,
        bool IsEnabled,
        Dictionary<string, string>? ConnectionSettings,
        Dictionary<string, string>? CustomOptions);

    private sealed record PlanSummaryResponse(string Id, string Name, string Description, bool IsEnabled, string MasterNodeId, string SyncItemType, int ExecutionCount, string? LastExecutedAt);

    private sealed record PlanDetailResponse(
        string Id,
        string Name,
        string Description,
        bool IsEnabled,
        string MasterNodeId,
        string SyncItemType,
        int ExecutionCount,
        DateTimeOffset? LastExecutedAt,
        string TriggerType,
        string? CronExpression,
        double? IntervalSeconds,
        bool EnableFileSystemWatcher,
        IReadOnlyList<PlanSlaveResponse> Slaves);

    private sealed record PlanSlaveResponse(
        string SlaveNodeId,
        string SyncMode,
        string? SourcePath,
        string? TargetPath,
        bool EnableDeletionProtection,
        string ConflictResolutionStrategy,
        IReadOnlyList<string> Filters,
        IReadOnlyList<string> Exclusions);

    private sealed record CreateOrUpdatePlanRequest(
        string Name,
        string? Description,
        string? MasterNodeId,
        string SyncItemType,
        bool IsEnabled,
        string TriggerType,
        string? CronExpression,
        double? IntervalSeconds,
        bool EnableFileSystemWatcher,
        IReadOnlyList<PlanSlaveRequest> Slaves);

    private sealed record PlanSlaveRequest(
        string SlaveNodeId,
        string SyncMode,
        string? SourcePath,
        string? TargetPath,
        bool EnableDeletionProtection,
        string? ConflictResolutionStrategy,
        IReadOnlyList<string> Filters,
        IReadOnlyList<string> Exclusions);

    private sealed record ExecutePlanResponse(string PlanId, int TotalTasks, int SuccessCount, int NoChangesCount, int ConflictCount, int FailedCount);

    private sealed record HistoryEntryResponse(string Id, string PlanId, string TaskId, string NodeId, string Path, string Name, long Size, string State, DateTimeOffset SyncTimestamp, long SyncVersion, string? Checksum);

    private sealed record ConfigSummaryResponse(string ServiceName, string ConfigurationFilePath, bool EnableSyncFramework, string HistoryStorePath, int NodeCount, int PlanCount, bool EnablePluginSystem, string PluginDirectory);
}

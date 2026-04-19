using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using UniversalSyncService.Testing;
using Xunit;

namespace UniversalSyncService.Host.IntegrationTests;

public sealed class WebConsoleHttpApiTests : IAsyncLifetime
{
    private const string PlansApiRoute = "/api/v1/plans";
    private const string NodesApiRoute = "/api/v1/nodes";

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

        var response = await client.GetAsync(PlansApiRoute);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Offline")]
    public async Task PlansApi_Should_ReturnConfiguredPlan_WithApiKey()
    {
        await WriteHostConfigAsync(createSourceFile: false);

        await using var factory = HostFactory.CreateHost(_contentRoot!.RootPath, enableWebRoot: true);
        var client = CreateAuthorizedClient(factory);

        var response = await client.GetFromJsonAsync<List<PlanSummaryResponse>>(PlansApiRoute);
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

        var request = CreatePlanRequest(
            name: "新增计划",
            description: "通过 HTTP API 创建",
            isEnabled: true,
            triggerType: "Manual",
            intervalSeconds: null,
            slave: CreatePlanSlaveRequest(
                slaveNodeId: "local-slave",
                syncMode: "Bidirectional",
                sourcePath: ".",
                targetPath: ".",
                enableDeletionProtection: true,
                conflictResolutionStrategy: null,
                filters: ["*.md"],
                exclusions: [".git/"]));

        var createResponse = await client.PostAsJsonAsync(PlansApiRoute, request);
        createResponse.EnsureSuccessStatusCode();

        var createdPlan = await createResponse.Content.ReadFromJsonAsync<PlanDetailResponse>();
        Assert.NotNull(createdPlan);
        Assert.Equal("新增计划", createdPlan.Name);
        Assert.Single(createdPlan.Slaves);
        Assert.Contains("*.md", createdPlan.Slaves[0].Filters);

        var plans = await client.GetFromJsonAsync<List<PlanSummaryResponse>>(PlansApiRoute);
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

        var request = CreatePlanRequest(
            name: "已更新计划",
            description: "更新后的计划描述",
            isEnabled: false,
            triggerType: "Scheduled",
            intervalSeconds: 300,
            slave: CreatePlanSlaveRequest(
                slaveNodeId: "local-slave",
                syncMode: "Push",
                sourcePath: "docs",
                targetPath: "backup",
                enableDeletionProtection: false,
                conflictResolutionStrategy: "KeepNewer",
                filters: ["*.txt", "*.md"],
                exclusions: ["bin/", "obj/"]));

        var updateResponse = await client.PutAsJsonAsync($"{PlansApiRoute}/local-filesystem-test", request);
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

        var request = CreatePlanRequest(
            name: "待删除计划",
            description: null,
            isEnabled: true,
            triggerType: "Manual",
            intervalSeconds: null,
            slave: CreatePlanSlaveRequest(
                slaveNodeId: "local-slave",
                syncMode: "Bidirectional",
                sourcePath: ".",
                targetPath: ".",
                enableDeletionProtection: true,
                conflictResolutionStrategy: null,
                filters: [],
                exclusions: []));

        var createResponse = await client.PostAsJsonAsync(PlansApiRoute, request);
        createResponse.EnsureSuccessStatusCode();
        var createdPlan = await createResponse.Content.ReadFromJsonAsync<PlanDetailResponse>();
        Assert.NotNull(createdPlan);

        var deleteResponse = await client.DeleteAsync($"{PlansApiRoute}/{createdPlan.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var detailResponse = await client.GetAsync($"{PlansApiRoute}/{createdPlan.Id}");
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

        var initialNodes = await client.GetFromJsonAsync<List<NodeSummaryResponse>>(NodesApiRoute);
        Assert.NotNull(initialNodes);
        Assert.Contains(initialNodes, node => node.Id == "host-local" && node.IsImplicitHostNode);
        Assert.Contains(initialNodes, node => node.Id == "local-slave" && Path.IsPathFullyQualified(node.RootPath ?? string.Empty));

        var createRequest = CreateNodeRequest(
            id: "local-archive",
            name: "归档节点",
            nodeType: "Local",
            isEnabled: true,
            rootPath: Path.Combine(_contentRoot!.RootPath, "archive"),
            customOptions: new Dictionary<string, string>
            {
                ["Color"] = "Blue"
            });

        var createResponse = await client.PostAsJsonAsync(NodesApiRoute, createRequest);
        createResponse.EnsureSuccessStatusCode();
        var createdNode = await createResponse.Content.ReadFromJsonAsync<NodeDetailResponse>();
        Assert.NotNull(createdNode);
        Assert.Equal("local-archive", createdNode.Id);
        Assert.False(createdNode.IsImplicitHostNode);
        Assert.Equal("已配置节点", createdNode.SourceLabel);
        Assert.True(Path.IsPathFullyQualified(createdNode.RootPath ?? string.Empty));
        Assert.True(Path.IsPathFullyQualified(createdNode.ConnectionSettings["RootPath"]));

        var updateRequest = CreateNodeRequest(
            id: null,
            name: "归档节点-已更新",
            nodeType: "Local",
            isEnabled: false,
            rootPath: Path.Combine(_contentRoot!.RootPath, "archive-updated"),
            customOptions: new Dictionary<string, string>
            {
                ["Tier"] = "Cold"
            });

        var updateResponse = await client.PutAsJsonAsync($"{NodesApiRoute}/local-archive", updateRequest);
        updateResponse.EnsureSuccessStatusCode();
        var updatedNode = await updateResponse.Content.ReadFromJsonAsync<NodeDetailResponse>();
        Assert.NotNull(updatedNode);
        Assert.Equal("归档节点-已更新", updatedNode.Name);
        Assert.False(updatedNode.IsEnabled);
        Assert.Equal("Cold", updatedNode.CustomOptions["Tier"]);
        Assert.True(Path.IsPathFullyQualified(updatedNode.RootPath ?? string.Empty));
        Assert.True(Path.IsPathFullyQualified(updatedNode.ConnectionSettings["RootPath"]));

        var deleteResponse = await client.DeleteAsync($"{NodesApiRoute}/local-archive");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var deletedNodeResponse = await client.GetAsync($"{NodesApiRoute}/local-archive");
        Assert.Equal(HttpStatusCode.NotFound, deletedNodeResponse.StatusCode);
    }

    [Fact]
    [Trait("Category", "Offline")]
    public async Task NodesApi_Should_RejectMismatchedOrImplicitNodeIdOnUpdate()
    {
        await WriteHostConfigAsync(createSourceFile: false);

        await using var factory = HostFactory.CreateHost(_contentRoot!.RootPath, enableWebRoot: true);
        var client = CreateAuthorizedClient(factory);

        var mismatchedRequest = CreateNodeRequest(
            id: "other-node",
            name: "不匹配节点",
            nodeType: "Local",
            isEnabled: true,
            rootPath: Path.Combine(_contentRoot!.RootPath, "other"),
            customOptions: new Dictionary<string, string>());

        var mismatchResponse = await client.PutAsJsonAsync($"{NodesApiRoute}/local-master", mismatchedRequest);
        Assert.Equal(HttpStatusCode.BadRequest, mismatchResponse.StatusCode);

        var implicitRequest = CreateNodeRequest(
            id: "host-local",
            name: "宿主节点",
            nodeType: "Local",
            isEnabled: true,
            rootPath: Path.Combine(_contentRoot!.RootPath, "host"),
            customOptions: new Dictionary<string, string>());

        var implicitResponse = await client.PutAsJsonAsync($"{NodesApiRoute}/local-master", implicitRequest);
        Assert.Equal(HttpStatusCode.BadRequest, implicitResponse.StatusCode);
    }

    private static CreateOrUpdatePlanRequest CreatePlanRequest(
        string name,
        string? description,
        bool isEnabled,
        string triggerType,
        double? intervalSeconds,
        PlanSlaveRequest slave)
    {
        return new CreateOrUpdatePlanRequest(
            Name: name,
            Description: description,
            MasterNodeId: "local-master",
            SyncItemType: "FileSystem",
            IsEnabled: isEnabled,
            TriggerType: triggerType,
            CronExpression: null,
            IntervalSeconds: intervalSeconds,
            EnableFileSystemWatcher: false,
            Slaves: [slave]);
    }

    private static PlanSlaveRequest CreatePlanSlaveRequest(
        string slaveNodeId,
        string syncMode,
        string? sourcePath,
        string? targetPath,
        bool enableDeletionProtection,
        string? conflictResolutionStrategy,
        IReadOnlyList<string> filters,
        IReadOnlyList<string> exclusions)
    {
        return new PlanSlaveRequest(
            SlaveNodeId: slaveNodeId,
            SyncMode: syncMode,
            SourcePath: sourcePath,
            TargetPath: targetPath,
            EnableDeletionProtection: enableDeletionProtection,
            ConflictResolutionStrategy: conflictResolutionStrategy,
            Filters: filters,
            Exclusions: exclusions);
    }

    private static CreateOrUpdateNodeRequest CreateNodeRequest(
        string? id,
        string name,
        string nodeType,
        bool isEnabled,
        string rootPath,
        Dictionary<string, string>? customOptions)
    {
        return new CreateOrUpdateNodeRequest(
            Id: id,
            Name: name,
            NodeType: nodeType,
            IsEnabled: isEnabled,
            ConnectionSettings: CreateRootConnectionSettings(rootPath),
            CustomOptions: customOptions);
    }

    private static Dictionary<string, string> CreateRootConnectionSettings(string rootPath)
    {
        return new Dictionary<string, string>
        {
            ["RootPath"] = rootPath.Replace("\\", "/")
        };
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

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UniversalSyncService.Abstractions.Nodes;
using UniversalSyncService.Abstractions.SyncItems;
using UniversalSyncService.Abstractions.SyncManagement.ConfigNodes;
using UniversalSyncService.Core.DependencyInjection;
using UniversalSyncService.Core.Nodes.OneDrive;
using UniversalSyncService.Core.Providers;
using UniversalSyncService.Core.SyncItems;
using UniversalSyncService.Testing;
using Xunit;
using OneDriveLaneConfiguration = UniversalSyncService.Testing.OneDriveTestBootstrap.OneDriveLaneConfiguration;

namespace UniversalSyncService.IntegrationTests.Nodes;

/// <summary>
/// 统一节点抽象一致性矩阵。
///
/// 目标：
/// 1) 用统一 contract/harness 覆盖所有已注册 concrete provider；
/// 2) 以 capability 为发现入口，而不是按 provider 类型分叉主断言链；
/// 3) 覆盖生命周期、身份标识删除、作用域边界与错误语义。
/// </summary>
public sealed class NodeConformanceMatrixTests
{
    private const string OneDriveTestConfigFileName = "onedrive.test.yaml";

    private static readonly NodeCapabilities[] RequiredOperationCapabilities =
    [
        NodeCapabilities.CanRead,
        NodeCapabilities.CanWrite,
        NodeCapabilities.CanDelete
    ];

    [Fact]
    [Trait("Category", "Offline")]
    public void Matrix_ShouldCoverAllRegisteredConcreteProviders()
    {
        using var scope = CreateCoreScope();
        var providers = scope.ServiceProvider.GetServices<INodeProvider>().ToList();
        var providerTypes = providers
            .Select(static provider => provider.ProviderType)
            .OrderBy(static providerType => providerType, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var mappedProviderTypes = NodeProviderCases.All
            .Select(static @case => @case.ProviderType)
            .OrderBy(static providerType => providerType, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal(providerTypes, mappedProviderTypes, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Offline")]
    public void Matrix_CapabilityDiscovery_ShouldReturnExpectedProviders()
    {
        using var scope = CreateCoreScope();
        var registry = scope.ServiceProvider.GetRequiredService<NodeProviderRegistry>();

        foreach (var capability in RequiredOperationCapabilities)
        {
            var providers = registry.GetProvidersByCapability(capability)
                .Select(static provider => provider.ProviderType)
                .OrderBy(static providerType => providerType, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var expected = NodeProviderCases.All
                .Where(@case => @case.ExpectedCapabilities.HasFlag(capability))
                .Select(@case => @case.ProviderType)
                .OrderBy(static providerType => providerType, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            Assert.Equal(expected, providers, StringComparer.OrdinalIgnoreCase);
        }
    }

    [Fact]
    [Trait("Category", "Offline")]
    public async Task Matrix_ProviderContracts_ShouldPassUnifiedAssertions()
    {
        using var scope = CreateCoreScope();

        foreach (var providerCase in NodeProviderCases.All)
        {
            var provider = ResolveProvider(scope.ServiceProvider, providerCase.ProviderType);
            var configuration = providerCase.CreateConfiguration();

            Assert.True(provider.CanCreate(configuration));

            var normalizedConfiguration = provider.NormalizeConfiguration(configuration);
            var (isValid, validationError) = provider.ValidateConfiguration(normalizedConfiguration);
            Assert.True(isValid, $"Provider {provider.ProviderType} 验证失败：{validationError}");

            Assert.True(provider.SupportsSyncItemKind(SyncItemKinds.FileSystem));

            foreach (var capability in RequiredOperationCapabilities)
            {
                Assert.True(provider.SupportsCapability(capability),
                    $"Provider {provider.ProviderType} 声称缺少必需能力 {capability}。");
            }

            foreach (var capability in Enum.GetValues<NodeCapabilities>())
            {
                if (capability == NodeCapabilities.None)
                {
                    continue;
                }

                var expected = providerCase.ExpectedCapabilities.HasFlag(capability);
                Assert.Equal(expected, provider.SupportsCapability(capability));
            }

            providerCase.AssertScopeBoundary(provider, normalizedConfiguration);

            var node = await provider.CreateAsync(normalizedConfiguration, CancellationToken.None);
            Assert.Equal(NodeState.Disconnected, node.State);
            Assert.True(node.SupportsSyncItemKind(SyncItemKinds.FileSystem));

            foreach (var capability in Enum.GetValues<NodeCapabilities>())
            {
                if (capability == NodeCapabilities.None)
                {
                    continue;
                }

                Assert.Equal(node.Capabilities.HasFlag(capability), node.SupportsCapability(capability));
            }
        }
    }

    [Fact]
    [Trait("Category", "Offline")]
    public async Task Matrix_InvalidConfigurations_ShouldFailValidationAndCreationConsistently()
    {
        using var scope = CreateCoreScope();

        foreach (var invalidCase in NodeInvalidConfigurationCases.All)
        {
            var provider = ResolveProvider(scope.ServiceProvider, invalidCase.ProviderType);
            var configuration = invalidCase.CreateInvalidConfiguration();

            var (isValid, _) = provider.ValidateConfiguration(configuration);
            Assert.False(isValid);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => provider.CreateAsync(configuration, CancellationToken.None));
        }
    }

    [Fact]
    [Trait("Category", "Offline")]
    public async Task Matrix_LocalNode_ShouldPassLifecycleAndIdentityOperations()
    {
        await ExecuteNodeLifecycleAndIdentityContractAsync(new LocalNodeConformanceCase());
    }

    [SkippableFact(Timeout = 180000)]
    [Trait("Category", "OnlineWarmAuth")]
    public async Task Matrix_OneDriveNode_ShouldPassLifecycleAndIdentityOperations()
    {
        await ExecuteNodeLifecycleAndIdentityContractAsync(new OneDriveNodeConformanceCase());
    }

    private static IServiceScope CreateCoreScope()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.ClearProviders());
        services.AddUniversalSyncCore();

        return services
            .BuildServiceProvider(validateScopes: false)
            .CreateScope();
    }

    private static INodeProvider ResolveProvider(IServiceProvider serviceProvider, string providerType)
    {
        return serviceProvider
            .GetRequiredService<IEnumerable<INodeProvider>>()
            .Single(provider => provider.ProviderType.Equals(providerType, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task ExecuteNodeLifecycleAndIdentityContractAsync(INodeConformanceCase conformanceCase)
    {
        await using var prepared = await conformanceCase.PrepareAsync();
        using var scope = CreateCoreScope();

        var provider = ResolveProvider(scope.ServiceProvider, conformanceCase.ProviderType);
        var normalizedConfiguration = provider.NormalizeConfiguration(prepared.Configuration);
        var node = await provider.CreateAsync(normalizedConfiguration, CancellationToken.None);

        var payloadIdentity = $"contract/{Guid.NewGuid():N}.txt";
        var payloadContent = $"node-conformance-payload-{DateTimeOffset.UtcNow:O}";
        var uploadItem = CreateInMemoryFileItem(payloadIdentity, payloadContent);

        try
        {
            Assert.Equal(NodeState.Disconnected, node.State);

            await node.ConnectAsync(CancellationToken.None);
            Assert.Equal(NodeState.Connected, node.State);

            Assert.True(node.SupportsSyncItemKind(SyncItemKinds.FileSystem));
            foreach (var capability in RequiredOperationCapabilities)
            {
                Assert.True(node.SupportsCapability(capability),
                    $"节点 {node.Metadata.Name} 缺少合同必需能力 {capability}。");
            }

            await node.UploadAsync(uploadItem, CancellationToken.None);

            var afterUpload = await EnumerateItemsByPathAsync(node, CancellationToken.None);
            Assert.True(afterUpload.ContainsKey(payloadIdentity), $"上传后未发现路径 {payloadIdentity}。");

            var uploadedItem = afterUpload[payloadIdentity];
            var downloadProbeIdentity = $"contract/download-probe-{Guid.NewGuid():N}.txt";
            var downloadProbeItem = CreateProxyFileItem(uploadedItem, downloadProbeIdentity);
            await node.DownloadAsync(downloadProbeItem, CancellationToken.None);

            if (node.Metadata.NodeType == NodeType.Local)
            {
                var afterDownload = await EnumerateItemsByPathAsync(node, CancellationToken.None);
                Assert.True(afterDownload.ContainsKey(downloadProbeIdentity),
                    $"本地节点下载探针未落盘：{downloadProbeIdentity}。");
            }

            Assert.False(string.IsNullOrWhiteSpace(uploadedItem.Identity));
            await node.DeleteAsync(uploadedItem.Identity, CancellationToken.None);
            await node.DeleteAsync(downloadProbeItem.Identity, CancellationToken.None);

            var afterDelete = await EnumerateItemsByPathAsync(node, CancellationToken.None);
            Assert.False(afterDelete.ContainsKey(payloadIdentity), $"删除后仍发现路径 {payloadIdentity}。");
            Assert.False(afterDelete.ContainsKey(downloadProbeIdentity), $"删除后仍发现路径 {downloadProbeIdentity}。");
        }
        finally
        {
            await prepared.CleanupConnectedNodeAsync(node);
            if (node.State != NodeState.Disconnected)
            {
                await node.DisconnectAsync(CancellationToken.None);
            }

        }
    }

    private static FileSystemSyncItem CreateInMemoryFileItem(string path, string content)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        var utcNow = DateTimeOffset.UtcNow;
        var metadata = new SyncItemMetadata(
            id: path,
            name: Path.GetFileName(path),
            path: path,
            size: bytes.LongLength,
            createdAt: utcNow,
            modifiedAt: utcNow,
            checksum: null,
            contentType: "text/plain");

        return new FileSystemSyncItem(
            metadata,
            SyncItemType.File,
            streamReaderFactory: _ => Task.FromResult<Stream>(new MemoryStream(bytes, writable: false)));
    }

    private static FileSystemSyncItem CreateProxyFileItem(ISyncItem sourceItem, string targetPath)
    {
        var sourceMetadata = sourceItem.Metadata;
        var metadata = new SyncItemMetadata(
            id: targetPath,
            name: Path.GetFileName(targetPath),
            path: targetPath,
            size: sourceMetadata.Size,
            createdAt: sourceMetadata.CreatedAt,
            modifiedAt: sourceMetadata.ModifiedAt,
            checksum: sourceMetadata.Checksum,
            contentType: sourceMetadata.ContentType);

        return new FileSystemSyncItem(
            metadata,
            SyncItemType.File,
            streamReaderFactory: ct => sourceItem.OpenReadAsync(ct));
    }

    private static async Task<Dictionary<string, ISyncItem>> EnumerateItemsByPathAsync(INode node, CancellationToken cancellationToken)
    {
        var items = new Dictionary<string, ISyncItem>(StringComparer.OrdinalIgnoreCase);

        await foreach (var item in node.GetSyncItemsAsync(cancellationToken))
        {
            items[item.Metadata.Path.Replace('\\', '/')] = item;
        }

        return items;
    }

    private interface INodeConformanceCase
    {
        string ProviderType { get; }

        Task<PreparedConformanceContext> PrepareAsync();
    }

    private sealed record PreparedConformanceContext(
        string WorkingDirectory,
        NodeConfiguration Configuration,
        Func<INode, Task> CleanupConnectedNodeAsync) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            try
            {
                if (Directory.Exists(WorkingDirectory))
                {
                    Directory.Delete(WorkingDirectory, recursive: true);
                }
            }
            catch
            {
                // 测试临时目录清理失败不影响断言。
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class LocalNodeConformanceCase : INodeConformanceCase
    {
        public string ProviderType => "Local";

        public Task<PreparedConformanceContext> PrepareAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"uss-local-node-conformance-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);

            var configuration = new NodeConfiguration(
                id: $"local-conformance-{Guid.NewGuid():N}",
                name: "Local Conformance Node",
                nodeType: "Local",
                connectionSettings: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["RootPath"] = root
                });

            return Task.FromResult(new PreparedConformanceContext(
                WorkingDirectory: root,
                Configuration: configuration,
                CleanupConnectedNodeAsync: static node => node.DisconnectAsync(CancellationToken.None)));
        }
    }

    private sealed class OneDriveNodeConformanceCase : INodeConformanceCase
    {
        public string ProviderType => "OneDrive";

        public async Task<PreparedConformanceContext> PrepareAsync()
        {
            var lane = await GetOnlineWarmAuthLaneConfigurationOrSkipAsync();
            var remoteRoot = $"/UniversalSyncTest/NodeConformance_{Guid.NewGuid():N}";
            var localWorkingDirectory = Path.Combine(Path.GetTempPath(), $"uss-onedrive-node-conformance-{Guid.NewGuid():N}");
            Directory.CreateDirectory(localWorkingDirectory);

            var configuration = new NodeConfiguration(
                id: $"onedrive-conformance-{Guid.NewGuid():N}",
                name: "OneDrive Conformance Node",
                nodeType: "OneDrive",
                connectionSettings: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["ClientId"] = lane.ClientId!,
                    ["TenantId"] = lane.TenantId,
                    ["AuthMode"] = "InteractiveBrowser",
                    ["RootPath"] = remoteRoot,
                    ["Scopes"] = "Files.ReadWrite offline_access User.Read"
                });

            return new PreparedConformanceContext(
                WorkingDirectory: localWorkingDirectory,
                Configuration: configuration,
                CleanupConnectedNodeAsync: async node =>
                {
                    if (node is OneDriveNode oneDriveNode && oneDriveNode.State == NodeState.Connected)
                    {
                        await oneDriveNode.DeleteConfiguredRootAsync(CancellationToken.None);
                    }
                });
        }
    }

    private static class NodeProviderCases
    {
        public static IReadOnlyList<NodeProviderContractCase> All { get; } =
        [
            new(
                ProviderType: "Local",
                ExpectedCapabilities: NodeCapabilities.CanRead | NodeCapabilities.CanWrite | NodeCapabilities.CanDelete | NodeCapabilities.CanStream,
                CreateConfiguration: CreateLocalProviderConfiguration,
                AssertScopeBoundary: AssertLocalScopeBoundary),
            new(
                ProviderType: "OneDrive",
                ExpectedCapabilities: NodeCapabilities.CanRead | NodeCapabilities.CanWrite | NodeCapabilities.CanDelete | NodeCapabilities.CanStream,
                CreateConfiguration: CreateOneDriveProviderConfiguration,
                AssertScopeBoundary: AssertOneDriveScopeBoundary)
        ];

        private static NodeConfiguration CreateLocalProviderConfiguration()
        {
            var root = Path.Combine(Path.GetTempPath(), $"uss-provider-local-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);

            return new NodeConfiguration(
                id: "local-provider-contract",
                name: "Local Provider Contract",
                nodeType: "Local",
                connectionSettings: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["RootPath"] = root
                });
        }

        private static NodeConfiguration CreateOneDriveProviderConfiguration()
        {
            return new NodeConfiguration(
                id: "onedrive-provider-contract",
                name: "OneDrive Provider Contract",
                nodeType: "OneDrive",
                connectionSettings: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["ClientId"] = "offline-conformance-client",
                    ["TenantId"] = "common",
                    ["AuthMode"] = "InteractiveBrowser",
                    ["RootPath"] = "/UniversalSyncTest/ProviderContract"
                });
        }

        private static void AssertLocalScopeBoundary(INodeProvider provider, NodeConfiguration configuration)
        {
            Assert.True(provider.SupportsScopeBoundary(configuration, null));
            Assert.True(provider.SupportsScopeBoundary(configuration, "nested/child"));
            Assert.True(provider.SupportsScopeBoundary(configuration, @"C:\\scope"));

            var resolvedRoot = provider.ResolveScopeBoundary(configuration, null);
            var resolvedNested = provider.ResolveScopeBoundary(configuration, "nested/child");
            var expectedNested = Path.GetFullPath(Path.Combine(resolvedRoot, "nested", "child"));

            Assert.Equal(Path.GetFullPath(resolvedRoot), Path.GetFullPath(provider.GetDisplayScopeBoundary(configuration)!));
            Assert.Equal(expectedNested, Path.GetFullPath(resolvedNested));
        }

        private static void AssertOneDriveScopeBoundary(INodeProvider provider, NodeConfiguration configuration)
        {
            Assert.True(provider.SupportsScopeBoundary(configuration, null));
            Assert.True(provider.SupportsScopeBoundary(configuration, "nested/path"));
            Assert.False(provider.SupportsScopeBoundary(configuration, @"C:\\scope"));

            var resolvedRoot = provider.ResolveScopeBoundary(configuration, null);
            var resolvedNested = provider.ResolveScopeBoundary(configuration, "nested/path");

            Assert.Equal("/UniversalSyncTest/ProviderContract", resolvedRoot);
            Assert.Equal("/UniversalSyncTest/ProviderContract/nested/path", resolvedNested);
            Assert.Equal("/UniversalSyncTest/ProviderContract", provider.GetDisplayScopeBoundary(configuration));

            Assert.Throws<InvalidOperationException>(() => provider.ResolveScopeBoundary(configuration, "../escape"));
            Assert.Throws<InvalidOperationException>(() => provider.ResolveScopeBoundary(configuration, @"C:\\absolute"));
        }
    }

    private static class NodeInvalidConfigurationCases
    {
        public static IReadOnlyList<NodeInvalidConfigurationCase> All { get; } =
        [
            new(
                ProviderType: "Local",
                CreateInvalidConfiguration: () => new NodeConfiguration(
                    id: "local-invalid",
                    name: "Local Invalid",
                    nodeType: "Local",
                    connectionSettings: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))),
            new(
                ProviderType: "OneDrive",
                CreateInvalidConfiguration: () => new NodeConfiguration(
                    id: "onedrive-invalid",
                    name: "OneDrive Invalid",
                    nodeType: "OneDrive",
                    connectionSettings: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["ClientId"] = "invalid-client",
                        ["TenantId"] = "common",
                        ["RootPath"] = @"C:\invalid-root"
                    }))
        ];
    }

    private sealed record NodeProviderContractCase(
        string ProviderType,
        NodeCapabilities ExpectedCapabilities,
        Func<NodeConfiguration> CreateConfiguration,
        Action<INodeProvider, NodeConfiguration> AssertScopeBoundary);

    private sealed record NodeInvalidConfigurationCase(
        string ProviderType,
        Func<NodeConfiguration> CreateInvalidConfiguration);

    private static async Task<OneDriveLaneConfiguration> GetOnlineWarmAuthLaneConfigurationOrSkipAsync()
    {
        var configuration = OneDriveTestBootstrap.ResolveLaneConfiguration(OneDriveTestConfigFileName);

        Skip.If(string.IsNullOrWhiteSpace(configuration.ClientId),
            "Node conformance matrix(OnlineWarmAuth) 前置检查失败：未解析到有效 ClientId。\n" +
            $"模板路径：{configuration.TemplatePath}\n" +
            $"本地覆盖路径：{configuration.LocalOverridePath}（存在={configuration.LocalOverrideExists}）\n" +
            "请先配置 OneDrive 本地凭据后重试。");

        try
        {
            await OneDriveTestBootstrap.EnsureWarmAuthRecordReadyAsync(configuration.ClientId!, CancellationToken.None);
        }
        catch (InvalidOperationException ex)
        {
            Skip.If(true,
                "Node conformance matrix(OnlineWarmAuth) 前置检查失败：缺少可复用持久化认证记录，按设计 Skip。\n" +
                ex.Message);
        }

        return configuration;
    }
}

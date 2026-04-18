using Microsoft.Extensions.Logging.Abstractions;
using UniversalSyncService.Abstractions.Nodes;
using UniversalSyncService.Abstractions.SyncItems;
using UniversalSyncService.Abstractions.SyncManagement.ConfigNodes;
using UniversalSyncService.Abstractions.SyncManagement.Engine;
using UniversalSyncService.Abstractions.SyncManagement.History;
using UniversalSyncService.Abstractions.SyncManagement.Plans;
using UniversalSyncService.Abstractions.SyncManagement.Tasks;
using UniversalSyncService.Core.Providers;
using UniversalSyncService.Core.SyncManagement.Engine;
using UniversalSyncService.Core.SyncManagement.Tasks;
using Xunit;

namespace UniversalSyncService.IntegrationTests;

public sealed class SyncEngineRegressionTests
{
    [Fact]
    public void CalculateDecision_ShouldTreatChecksumDifferenceAsModification_WhenHistoryTimestampMatches()
    {
        // Arrange
        var historyMetadata = CreateMetadata(
            path: "note.txt",
            size: 10,
            modifiedAt: new DateTimeOffset(2026, 4, 14, 0, 0, 0, TimeSpan.Zero),
            checksum: "OLD-HASH");
        var currentMasterMetadata = CreateMetadata(
            path: "note.txt",
            size: 10,
            modifiedAt: historyMetadata.ModifiedAt,
            checksum: "NEW-HASH");
        var currentSlaveMetadata = historyMetadata;
        var historyEntry = CreateHistoryEntry("plan-1", "node-1", historyMetadata, FileHistoryState.Exists);

        var context = new SyncPathSyncContext(
            "note.txt",
            currentMasterMetadata,
            currentSlaveMetadata,
            historyEntry,
            CreateHistoryEntry("plan-1", "node-2", historyMetadata, FileHistoryState.Exists));

        var engine = new SyncAlgorithmEngine(NullLogger<SyncAlgorithmEngine>.Instance);

        // Act
        var decision = engine.CalculateDecision(context, SyncMode.Push);

        // Assert
        Assert.Equal(SyncDecision.Push, decision);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldDeleteRemoteUsingGenericNodeContract()
    {
        // Arrange
        var masterConfig = CreateNodeConfiguration("master", "Local");
        var slaveConfig = CreateNodeConfiguration("slave", "OneDrive");
        var masterNode = new TestNode("master", NodeType.Local);
        var slaveNode = new TestNode("slave", NodeType.Cloud);
        var runner = new FileSystemSyncTaskRunner(
            new NodeProviderRegistry([new TestNodeProvider(masterConfig, masterNode), new TestNodeProvider(slaveConfig, slaveNode)]),
            new StubSyncAlgorithmEngine("obsolete.txt", SyncDecision.DeleteRemote),
            new StubConflictResolver(),
            new InMemoryHistoryManager(
                previousEntries:
                [
                    CreateHistoryEntry(
                        planId: "plan-1",
                        nodeId: slaveConfig.Id,
                        metadata: CreateMetadata("obsolete.txt", 5, DateTimeOffset.UtcNow, checksum: null),
                        state: FileHistoryState.Exists)
                ]),
            NullLogger<FileSystemSyncTaskRunner>.Instance);

        var task = new SyncTask(
            id: "task-1",
            planId: "plan-1",
            masterNode: masterConfig,
            slaveNode: slaveConfig,
            syncMode: SyncMode.PushAndDelete,
            syncItemType: "FileSystem",
            sourcePath: null,
            targetPath: null,
            conflictResolutionStrategy: ConflictResolutionStrategy.Manual,
            executionRequirement: TaskExecutionRequirement.Ready,
            executeCoreAsync: (syncTask, ct) => runner.ExecuteAsync(syncTask, ct),
            logger: NullLogger<SyncTask>.Instance);

        // Act
        var result = await task.ExecuteAsync(CancellationToken.None);

        // Assert
        Assert.Equal(SyncTaskResult.Success, result);
        Assert.Equal(["obsolete.txt"], slaveNode.DeletedPaths);
    }

    private static NodeConfiguration CreateNodeConfiguration(string id, string nodeType)
    {
        return new NodeConfiguration(id, id, nodeType, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    private static SyncItemMetadata CreateMetadata(string path, long size, DateTimeOffset? modifiedAt, string? checksum)
    {
        return new SyncItemMetadata(path, Path.GetFileName(path), path, size, modifiedAt, modifiedAt, checksum, "application/octet-stream");
    }

    private static SyncHistoryEntry CreateHistoryEntry(string planId, string nodeId, SyncItemMetadata metadata, FileHistoryState state)
    {
        return new SyncHistoryEntry(Guid.NewGuid().ToString("N"), planId, "task-history", nodeId, metadata, state, DateTimeOffset.UtcNow, 1);
    }

    private sealed class StubSyncAlgorithmEngine(string path, SyncDecision decision) : ISyncAlgorithmEngine
    {
        public SyncDecision CalculateDecision(SyncPathSyncContext context, SyncMode syncMode)
        {
            return context.Path.Equals(path, StringComparison.OrdinalIgnoreCase) ? decision : SyncDecision.DoNothing;
        }

        public Task<Dictionary<string, SyncDecision>> CalculateDecisionsAsync(IEnumerable<SyncPathSyncContext> contexts, SyncMode syncMode)
        {
            return Task.FromResult(contexts.ToDictionary(context => context.Path, context => CalculateDecision(context, syncMode), StringComparer.OrdinalIgnoreCase));
        }
    }

#pragma warning disable CS0067
    private sealed class StubConflictResolver : IConflictResolver
    {
        public ConflictResolutionStrategy DefaultStrategy { get; set; } = ConflictResolutionStrategy.Manual;

        public event Action<ISyncConflict>? OnConflictDetected;

        public event Action<ISyncConflict, SyncDecision>? OnConflictResolved;

        public Task<SyncDecision> ResolveAsync(ISyncConflict conflict, ConflictResolutionStrategy? strategy = null)
        {
            return Task.FromResult(SyncDecision.Conflict);
        }

        public Task<Dictionary<string, SyncDecision>> ResolveBatchAsync(IEnumerable<ISyncConflict> conflicts, ConflictResolutionStrategy strategy)
        {
            return Task.FromResult(new Dictionary<string, SyncDecision>(StringComparer.OrdinalIgnoreCase));
        }
    }
#pragma warning restore CS0067

    private sealed class InMemoryHistoryManager(IEnumerable<SyncHistoryEntry>? previousEntries = null) : ISyncHistoryManager
    {
        private readonly List<SyncHistoryEntry> _previousEntries = previousEntries?.ToList() ?? [];
        private readonly List<SyncHistoryEntry> _savedEntries = [];

        public Task<long> GetLatestVersionAsync(string planId)
        {
            return Task.FromResult(0L);
        }

        public Task<IReadOnlyList<SyncHistoryEntry>> GetPreviousSyncHistoryAsync(string planId, string nodeId)
        {
            IReadOnlyList<SyncHistoryEntry> result = _previousEntries
                .Where(entry => entry.PlanId == planId && entry.NodeId == nodeId)
                .ToList();
            return Task.FromResult(result);
        }

        public Task<SyncHistoryEntry?> GetFileHistoryAsync(string planId, string nodeId, string filePath)
        {
            return Task.FromResult(_previousEntries.FirstOrDefault(entry => entry.PlanId == planId && entry.NodeId == nodeId && entry.Metadata.Path == filePath));
        }

        public Task SaveHistoryAsync(IEnumerable<SyncHistoryEntry> entries)
        {
            _savedEntries.AddRange(entries);
            return Task.CompletedTask;
        }

        public Task CleanupOldHistoryAsync(string planId, int keepVersions)
        {
            return Task.CompletedTask;
        }

        public Task DeletePlanHistoryAsync(string planId)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SyncHistoryEntry>> GetRecentHistoryAsync(string? planId, int limit)
        {
            IReadOnlyList<SyncHistoryEntry> result = _savedEntries.Take(limit).ToList();
            return Task.FromResult(result);
        }
    }

    private sealed class TestNodeProvider(NodeConfiguration configuration, TestNode node) : INodeProvider
    {
        public bool CanCreate(NodeConfiguration candidate)
        {
            return candidate.Id == configuration.Id;
        }

        public Task<INode> CreateAsync(NodeConfiguration candidate, CancellationToken cancellationToken)
        {
            return Task.FromResult<INode>(node);
        }

        public NodeConfiguration NormalizeConfiguration(NodeConfiguration candidate)
        {
            return candidate;
        }

        public (bool IsValid, string? ErrorMessage) ValidateConfiguration(NodeConfiguration candidate)
        {
            return (true, null);
        }

        public Task EnsureAuthenticatedAsync(NodeConfiguration candidate, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public bool SupportsAbsoluteScopedPath(NodeConfiguration configuration)
        {
            return false;
        }

        public string ResolveScopedRoot(NodeConfiguration configuration, string? scopedPath)
        {
            return string.Empty;
        }

        public string? GetDisplayRootPath(NodeConfiguration configuration)
        {
            return string.Empty;
        }
    }

    private sealed class TestNode(string id, NodeType nodeType) : INode
    {
        public NodeMetadata Metadata { get; } = new(id, id, nodeType, "1.0.0", "test");

        public NodeCapabilities Capabilities { get; } = NodeCapabilities.CanRead | NodeCapabilities.CanWrite | NodeCapabilities.CanDelete;

        public NodeState State { get; private set; } = NodeState.Disconnected;

        public List<string> DeletedPaths { get; } = [];

        public Task ConnectAsync(CancellationToken cancellationToken)
        {
            State = NodeState.Connected;
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken)
        {
            State = NodeState.Disconnected;
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<ISyncItem> GetSyncItemsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task UploadAsync(ISyncItem item, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task DownloadAsync(ISyncItem item, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string relativePath, CancellationToken cancellationToken)
        {
            DeletedPaths.Add(relativePath);
            return Task.CompletedTask;
        }
    }
}

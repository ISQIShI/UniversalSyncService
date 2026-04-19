using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.FileProviders;
using System.Collections.Concurrent;
using UniversalSyncService.Abstractions.Configuration;
using UniversalSyncService.Abstractions.Nodes;
using UniversalSyncService.Abstractions.SyncItems;
using UniversalSyncService.Abstractions.SyncManagement.ConfigNodes;
using UniversalSyncService.Abstractions.SyncManagement.Engine;
using UniversalSyncService.Abstractions.SyncManagement.History;
using UniversalSyncService.Abstractions.SyncManagement.Plans;
using UniversalSyncService.Abstractions.SyncManagement.Tasks;
using UniversalSyncService.Core.Providers;
using UniversalSyncService.Core.SyncManagement.Engine;
using UniversalSyncService.Core.SyncManagement.History;
using UniversalSyncService.Core.SyncManagement.Tasks;
using UniversalSyncService.Testing;
using Xunit;

namespace UniversalSyncService.IntegrationTests;

[Trait("Category", "Offline")]
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

    [Fact]
    public void CalculateDecision_ShouldPullInPullMode_WhenSlaveModifiedAndMasterUnchanged()
    {
        // Arrange
        var baseline = CreateMetadata("note.txt", 10, new DateTimeOffset(2026, 4, 15, 0, 0, 0, TimeSpan.Zero), "base");
        var slaveModified = CreateMetadata("note.txt", 20, baseline.ModifiedAt?.AddMinutes(1), "changed");
        var context = new SyncPathSyncContext(
            "note.txt",
            baseline,
            slaveModified,
            CreateHistoryEntry("plan-pull", "master", baseline, FileHistoryState.Exists),
            CreateHistoryEntry("plan-pull", "slave", baseline, FileHistoryState.Exists));
        var engine = new SyncAlgorithmEngine(NullLogger<SyncAlgorithmEngine>.Instance);

        // Act
        var decision = engine.CalculateDecision(context, SyncMode.Pull);

        // Assert
        Assert.Equal(SyncDecision.Pull, decision);
    }

    [Fact]
    public void CalculateDecision_ShouldDeleteLocalInPullAndDeleteMode_WhenSlaveDeleted()
    {
        // Arrange
        var baseline = CreateMetadata("stale.txt", 11, new DateTimeOffset(2026, 4, 15, 0, 0, 0, TimeSpan.Zero), "same");
        var context = new SyncPathSyncContext(
            "stale.txt",
            baseline,
            null,
            CreateHistoryEntry("plan-pull-delete", "master", baseline, FileHistoryState.Exists),
            CreateHistoryEntry("plan-pull-delete", "slave", baseline, FileHistoryState.Exists));
        var engine = new SyncAlgorithmEngine(NullLogger<SyncAlgorithmEngine>.Instance);

        // Act
        var decision = engine.CalculateDecision(context, SyncMode.PullAndDelete);

        // Assert
        Assert.Equal(SyncDecision.DeleteLocal, decision);
    }

    [Theory]
    [InlineData(SyncMode.Pull)]
    [InlineData(SyncMode.PullAndDelete)]
    public void CalculateDecision_ShouldReturnCleanHistory_WhenBothSidesDeleted(SyncMode syncMode)
    {
        // Arrange
        var baseline = CreateMetadata("deleted.txt", 5, new DateTimeOffset(2026, 4, 15, 0, 0, 0, TimeSpan.Zero), "same");
        var context = new SyncPathSyncContext(
            "deleted.txt",
            null,
            null,
            CreateHistoryEntry("plan-clean", "master", baseline, FileHistoryState.Exists),
            CreateHistoryEntry("plan-clean", "slave", baseline, FileHistoryState.Exists));
        var engine = new SyncAlgorithmEngine(NullLogger<SyncAlgorithmEngine>.Instance);

        // Act
        var decision = engine.CalculateDecision(context, syncMode);

        // Assert
        Assert.Equal(SyncDecision.CleanHistory, decision);
    }

    [Fact]
    public async Task ResolveAsync_ShouldPush_WhenKeepLargerAndMasterIsLarger()
    {
        // Arrange
        var resolver = new ConflictResolver(NullLogger<ConflictResolver>.Instance);
        var conflict = new SyncConflict(
            "size.txt",
            new SyncItemFileStateSnapshot("size.txt", 100, DateTimeOffset.UtcNow, null),
            new SyncItemFileStateSnapshot("size.txt", 10, DateTimeOffset.UtcNow, null),
            null,
            null,
            "size conflict");

        // Act
        var decision = await resolver.ResolveAsync(conflict, ConflictResolutionStrategy.KeepLarger);

        // Assert
        Assert.Equal(SyncDecision.Push, decision);
    }

    [Fact]
    public async Task ResolveAsync_ShouldPull_WhenKeepLargerAndSlaveIsLarger()
    {
        // Arrange
        var resolver = new ConflictResolver(NullLogger<ConflictResolver>.Instance);
        var conflict = new SyncConflict(
            "size.txt",
            new SyncItemFileStateSnapshot("size.txt", 10, DateTimeOffset.UtcNow, null),
            new SyncItemFileStateSnapshot("size.txt", 100, DateTimeOffset.UtcNow, null),
            null,
            null,
            "size conflict");

        // Act
        var decision = await resolver.ResolveAsync(conflict, ConflictResolutionStrategy.KeepLarger);

        // Assert
        Assert.Equal(SyncDecision.Pull, decision);
    }

    [Fact]
    public async Task ResolveAsync_ShouldReturnConflict_WhenKeepLargerAndSizesEqual()
    {
        // Arrange
        var resolver = new ConflictResolver(NullLogger<ConflictResolver>.Instance);
        var conflict = new SyncConflict(
            "size.txt",
            new SyncItemFileStateSnapshot("size.txt", 100, DateTimeOffset.UtcNow, null),
            new SyncItemFileStateSnapshot("size.txt", 100, DateTimeOffset.UtcNow, null),
            null,
            null,
            "size conflict");

        // Act
        var decision = await resolver.ResolveAsync(conflict, ConflictResolutionStrategy.KeepLarger);

        // Assert
        Assert.Equal(SyncDecision.Conflict, decision);
    }

    [Fact]
    public async Task ResolveAsync_ShouldReturnConflictRename_WhenStrategyIsRenameBoth()
    {
        // Arrange
        var resolver = new ConflictResolver(NullLogger<ConflictResolver>.Instance);
        var conflict = new SyncConflict(
            "conflict.txt",
            new SyncItemFileStateSnapshot("conflict.txt", 10, DateTimeOffset.UtcNow, "master"),
            new SyncItemFileStateSnapshot("conflict.txt", 10, DateTimeOffset.UtcNow, "slave"),
            null,
            null,
            "rename both");

        // Act
        var decision = await resolver.ResolveAsync(conflict, ConflictResolutionStrategy.RenameBoth);

        // Assert
        Assert.Equal(SyncDecision.ConflictRename, decision);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldDeleteLocalUsingGenericNodeContract()
    {
        // Arrange
        var masterConfig = CreateNodeConfiguration("master", "OneDrive");
        var slaveConfig = CreateNodeConfiguration("slave", "Local");
        var masterNode = new TestNode("master", NodeType.Cloud);
        var slaveNode = new TestNode("slave", NodeType.Local);
        var runner = new FileSystemSyncTaskRunner(
            new NodeProviderRegistry([new TestNodeProvider(masterConfig, masterNode), new TestNodeProvider(slaveConfig, slaveNode)]),
            new StubSyncAlgorithmEngine("obsolete.txt", SyncDecision.DeleteLocal),
            new StubConflictResolver(),
            new InMemoryHistoryManager(
                previousEntries:
                [
                    CreateHistoryEntry(
                        planId: "plan-1",
                        nodeId: masterConfig.Id,
                        metadata: CreateMetadata("obsolete.txt", 5, DateTimeOffset.UtcNow, checksum: null),
                        state: FileHistoryState.Exists)
                ]),
            NullLogger<FileSystemSyncTaskRunner>.Instance);

        var task = new SyncTask(
            id: "task-delete-local",
            planId: "plan-1",
            masterNode: masterConfig,
            slaveNode: slaveConfig,
            syncMode: SyncMode.PullAndDelete,
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
        Assert.Equal(["obsolete.txt"], masterNode.DeletedPaths);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldPersistDeletedAnchorsForBothNodes_WithNextVersion_WhenDecisionIsCleanHistory()
    {
        // Arrange
        var masterConfig = CreateNodeConfiguration("master", "Local");
        var slaveConfig = CreateNodeConfiguration("slave", "Local");
        var masterNode = new TestNode("master", NodeType.Local);
        var slaveNode = new TestNode("slave", NodeType.Local);
        var previousMetadata = CreateMetadata("legacy.txt", 7, DateTimeOffset.UtcNow.AddMinutes(-5), checksum: "old");
        var historyManager = new InMemoryHistoryManager(
            previousEntries:
            [
                CreateHistoryEntry("plan-history", masterConfig.Id, previousMetadata, FileHistoryState.Exists),
                CreateHistoryEntry("plan-history", slaveConfig.Id, previousMetadata, FileHistoryState.Exists)
            ],
            latestVersion: 41);
        var runner = new FileSystemSyncTaskRunner(
            new NodeProviderRegistry([new TestNodeProvider(masterConfig, masterNode), new TestNodeProvider(slaveConfig, slaveNode)]),
            new StubSyncAlgorithmEngine("legacy.txt", SyncDecision.CleanHistory),
            new StubConflictResolver(),
            historyManager,
            NullLogger<FileSystemSyncTaskRunner>.Instance);

        var task = new SyncTask(
            id: "task-clean-history",
            planId: "plan-history",
            masterNode: masterConfig,
            slaveNode: slaveConfig,
            syncMode: SyncMode.Pull,
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
        var deletedEntries = historyManager.SavedEntries
            .Where(entry => entry.PlanId == "plan-history" && entry.Metadata.Path == "legacy.txt")
            .ToList();
        Assert.Equal(2, deletedEntries.Count);
        Assert.All(deletedEntries, entry => Assert.Equal(FileHistoryState.Deleted, entry.State));
        Assert.Contains(deletedEntries, entry => entry.NodeId == masterConfig.Id);
        Assert.Contains(deletedEntries, entry => entry.NodeId == slaveConfig.Id);
        Assert.All(deletedEntries, entry => Assert.Equal(42, entry.SyncVersion));
    }

    [Fact]
    public async Task SyncHistoryManager_ShouldTrimHistoryToConfiguredRetentionVersions()
    {
        // Arrange
        await using var tempRoot = await TempContentRoot.CreateAsync("UniversalSyncService-Regression");
        var manager = CreateSqliteHistoryManager(tempRoot.RootPath, keepVersions: 2);
        await manager.SaveHistoryAsync([CreateHistoryEntryWithVersion("plan-trim", "node-a", "a.txt", version: 1)]);
        await manager.SaveHistoryAsync([CreateHistoryEntryWithVersion("plan-trim", "node-a", "a.txt", version: 2)]);
        await manager.SaveHistoryAsync([CreateHistoryEntryWithVersion("plan-trim", "node-a", "a.txt", version: 3)]);

        // Act
        var history = await manager.GetPreviousSyncHistoryAsync("plan-trim", "node-a");
        var versions = history.Select(entry => entry.SyncVersion).Distinct().ToArray();

        // Assert
        Assert.Equal([3L, 2L], versions);
    }

    [Fact]
    public async Task SyncHistoryManager_CleanupOldHistory_ShouldKeepLatestRequestedVersions()
    {
        // Arrange
        await using var tempRoot = await TempContentRoot.CreateAsync("UniversalSyncService-Regression");
        var manager = CreateSqliteHistoryManager(tempRoot.RootPath, keepVersions: 10);
        await manager.SaveHistoryAsync([CreateHistoryEntryWithVersion("plan-cleanup", "node-a", "a.txt", version: 1)]);
        await manager.SaveHistoryAsync([CreateHistoryEntryWithVersion("plan-cleanup", "node-a", "a.txt", version: 2)]);
        await manager.SaveHistoryAsync([CreateHistoryEntryWithVersion("plan-cleanup", "node-a", "a.txt", version: 3)]);
        await manager.SaveHistoryAsync([CreateHistoryEntryWithVersion("plan-cleanup", "node-a", "a.txt", version: 4)]);

        // Act
        await manager.CleanupOldHistoryAsync("plan-cleanup", keepVersions: 2);
        var history = await manager.GetPreviousSyncHistoryAsync("plan-cleanup", "node-a");
        var versions = history.Select(entry => entry.SyncVersion).Distinct().ToArray();

        // Assert
        Assert.Equal([4L, 3L], versions);
    }

    [Fact]
    public async Task SyncHistoryManager_DeletePlanHistory_ShouldDeleteOnlyTargetPlan()
    {
        // Arrange
        await using var tempRoot = await TempContentRoot.CreateAsync("UniversalSyncService-Regression");
        var manager = CreateSqliteHistoryManager(tempRoot.RootPath, keepVersions: 10);
        await manager.SaveHistoryAsync([CreateHistoryEntryWithVersion("plan-a", "node-a", "a.txt", version: 1)]);
        await manager.SaveHistoryAsync([CreateHistoryEntryWithVersion("plan-b", "node-a", "b.txt", version: 1)]);

        // Act
        await manager.DeletePlanHistoryAsync("plan-a");

        // Assert
        Assert.Empty(await manager.GetPreviousSyncHistoryAsync("plan-a", "node-a"));
        Assert.NotEmpty(await manager.GetPreviousSyncHistoryAsync("plan-b", "node-a"));
    }

    [Fact]
    public async Task CancelTaskAsync_ShouldCancelRunningTask_AndCleanupActiveBookkeeping()
    {
        // Arrange
        var task = new TestSyncTask("task-1", "plan-1");
        var taskGenerator = new TestSyncTaskGenerator([task]);
        var taskExecutor = new BlockingTaskExecutor();
        var engine = new SyncEngine(taskGenerator, taskExecutor, NullLogger<SyncEngine>.Instance);

        // Act
        var execution = engine.ExecuteTaskAsync(task, CancellationToken.None);
        await taskExecutor.WaitForTaskStartedAsync("task-1");
        await WaitUntilAsync(() => engine.ActiveTasks.Any(active => active.Id == "task-1"));

        await engine.CancelTaskAsync("task-1");
        var result = await execution;

        // Assert
        Assert.Equal(SyncTaskResult.Cancelled, result);
        Assert.Empty(engine.ActiveTasks);
    }

    [Fact]
    public async Task CancelPlanAsync_ShouldCancelAllRunningTasksInPlan()
    {
        // Arrange
        var task1 = new TestSyncTask("task-1", "plan-1");
        var task2 = new TestSyncTask("task-2", "plan-1");
        var taskGenerator = new TestSyncTaskGenerator([task1, task2]);
        var taskExecutor = new BlockingTaskExecutor();
        var engine = new SyncEngine(taskGenerator, taskExecutor, NullLogger<SyncEngine>.Instance);

        // Act
        var execution = engine.ExecutePlanAsync(CreatePlan("plan-1"), CancellationToken.None);
        await taskExecutor.WaitForTaskStartedAsync("task-1");
        await taskExecutor.WaitForTaskStartedAsync("task-2");

        await engine.CancelPlanAsync("plan-1");
        var results = await execution;

        // Assert
        Assert.Equal(2, results.Count);
        Assert.All(results.Values, result => Assert.Equal(SyncTaskResult.Cancelled, result));
        Assert.Empty(engine.ActiveTasks);
    }

    [Fact]
    public async Task PauseExecution_ShouldBlockStartUntilResumeExecution()
    {
        // Arrange
        var executor = new SyncTaskExecutor(new TestConfigurationManagementService(), NullLogger<SyncTaskExecutor>.Instance);
        var task = new ProgressReportingTask("task-pause", "plan-pause");

        await executor.PauseExecution();

        // Act
        var execution = executor.ExecuteAsync(task, progress: null, CancellationToken.None);

        // Assert paused state blocks task start
        await Assert.ThrowsAsync<TimeoutException>(async () => await task.Started.Task.WaitAsync(TimeSpan.FromMilliseconds(250)));

        await executor.ResumeExecution();
        await task.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        task.AllowCompletion.TrySetResult(true);

        var result = await execution;
        Assert.Equal(SyncTaskResult.Success, result);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldNotAccumulateProgressSubscriptionsAcrossRuns()
    {
        // Arrange
        var executor = new SyncTaskExecutor(new TestConfigurationManagementService(), NullLogger<SyncTaskExecutor>.Instance);
        var task = new ProgressReportingTask("task-progress", "plan-progress");
        var progressEventCount = 0;
        executor.OnTaskProgressChanged += (_, _) => Interlocked.Increment(ref progressEventCount);

        // Act
        task.ResetForNextExecution();
        task.AllowCompletion.TrySetResult(true);
        var firstResult = await executor.ExecuteAsync(task, progress: null, CancellationToken.None);

        task.ResetForNextExecution();
        task.AllowCompletion.TrySetResult(true);
        var secondResult = await executor.ExecuteAsync(task, progress: null, CancellationToken.None);

        // Assert
        Assert.Equal(SyncTaskResult.Success, firstResult);
        Assert.Equal(SyncTaskResult.Success, secondResult);
        Assert.Equal(2, progressEventCount);
    }

    private static SyncPlan CreatePlan(string planId)
    {
        return new SyncPlan(
            planId,
            "计划",
            "master",
            "FileSystem",
            [
                new SyncPlanSlaveConfiguration("slave", SyncMode.Bidirectional)
            ],
            new SyncSchedule(SyncTriggerType.Manual));
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("在预期时间内未满足断言条件。");
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

    private static SyncHistoryEntry CreateHistoryEntryWithVersion(string planId, string nodeId, string path, long version)
    {
        var metadata = CreateMetadata(path, version, DateTimeOffset.UtcNow.AddMinutes(version), $"checksum-{version}");
        return new SyncHistoryEntry(Guid.NewGuid().ToString("N"), planId, $"task-{version}", nodeId, metadata, FileHistoryState.Exists, DateTimeOffset.UtcNow, version);
    }

    private static SyncHistoryManager CreateSqliteHistoryManager(string contentRoot, int keepVersions)
    {
        var options = new SyncFrameworkOptions
        {
            HistoryRetentionVersions = keepVersions,
            HistoryStorePath = Path.Combine(contentRoot, "history.db")
        };

        return new SyncHistoryManager(
            new HistoryTestConfigurationManagementService(options),
            new TestHostEnvironment(contentRoot),
            NullLogger<SyncHistoryManager>.Instance);
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

    private sealed class InMemoryHistoryManager(IEnumerable<SyncHistoryEntry>? previousEntries = null, long latestVersion = 0) : ISyncHistoryManager
    {
        private readonly List<SyncHistoryEntry> _previousEntries = previousEntries?.ToList() ?? [];
        private readonly List<SyncHistoryEntry> _savedEntries = [];
        private readonly long _latestVersion = latestVersion;

        public IReadOnlyList<SyncHistoryEntry> SavedEntries => _savedEntries;

        public Task<long> GetLatestVersionAsync(string planId)
        {
            var savedVersion = _savedEntries
                .Where(entry => entry.PlanId == planId)
                .Select(entry => entry.SyncVersion)
                .DefaultIfEmpty(0)
                .Max();
            return Task.FromResult(Math.Max(_latestVersion, savedVersion));
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

    private sealed class HistoryTestConfigurationManagementService(SyncFrameworkOptions syncOptions) : IConfigurationManagementService
    {
        public string ConfigurationFilePath => Path.Combine(Path.GetTempPath(), "history-test.yaml");

        public string GenerateDefaultYaml()
        {
            throw new NotSupportedException();
        }

        public ServiceOptions GetServiceOptions()
        {
            throw new NotSupportedException();
        }

        public LoggingOptions GetLoggingOptions()
        {
            throw new NotSupportedException();
        }

        public InterfaceOptions GetInterfaceOptions()
        {
            throw new NotSupportedException();
        }

        public PluginSystemOptions GetPluginSystemOptions()
        {
            throw new NotSupportedException();
        }

        public SyncFrameworkOptions GetSyncOptions()
        {
            return syncOptions;
        }

        public AppConfigurationDocument GetCurrentConfiguration()
        {
            throw new NotSupportedException();
        }

        public Task EnsureDefaultConfigurationFileAsync(CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<AppConfigurationDocument> LoadAsync(CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task SaveAsync(AppConfigurationDocument configuration, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;

        public string ApplicationName { get; set; } = "UniversalSyncService.IntegrationTests";

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(contentRootPath);
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

    private sealed class TestSyncTask(string id, string planId) : ISyncTask
    {
        public string Id { get; } = id;

        public string PlanId { get; } = planId;

        public NodeConfiguration MasterNode { get; } = CreateNodeConfiguration("master", "Local");

        public NodeConfiguration SlaveNode { get; } = CreateNodeConfiguration("slave", "Local");

        public SyncMode SyncMode => SyncMode.Bidirectional;

        public string SyncItemType => "FileSystem";

        public string? SourcePath => null;

        public string? TargetPath => null;

        public ConflictResolutionStrategy ConflictResolutionStrategy => ConflictResolutionStrategy.Manual;

        public SyncTaskState State => SyncTaskState.Pending;

        public SyncTaskResult? Result => null;

        public ISyncTaskProgress? Progress => null;

        public DateTimeOffset? StartTime => null;

        public DateTimeOffset? CompletionTime => null;

        public IReadOnlyList<string>? Errors => null;

        public event Action<ISyncTask, SyncTaskState>? OnStateChanged;

        public event Action<ISyncTask, ISyncTaskProgress>? OnProgressChanged;

        public event Action<ISyncTask, SyncTaskResult>? OnCompleted;

        public void ReportProgress(int processedFiles, int totalFiles, long transferredBytes, long totalBytes, string? currentFilePath, string currentOperation)
        {
        }

        public void AddError(string error)
        {
        }

        public Task<SyncTaskResult> ExecuteAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(SyncTaskResult.Success);
        }

        public Task PauseAsync()
        {
            return Task.CompletedTask;
        }

        public Task ResumeAsync()
        {
            return Task.CompletedTask;
        }

        public Task CancelAsync()
        {
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingTaskExecutor : ISyncTaskExecutor
    {
        private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _startedSignals = new(StringComparer.OrdinalIgnoreCase);

        public int MaxConcurrentTasks { get; set; } = 4;

        public bool IsPaused => false;

        public int QueuedTaskCount => 0;

        public event Action<ISyncTask>? OnTaskStarted;

        public event Action<ISyncTask, SyncTaskResult>? OnTaskCompleted;

        public event Action<ISyncTask, Exception>? OnTaskFailed;

        public event Action<ISyncTask, ISyncTaskProgress>? OnTaskProgressChanged;

        public async Task<SyncTaskResult> ExecuteAsync(ISyncTask task, IProgress<ISyncTaskProgress>? progress, CancellationToken cancellationToken)
        {
            var startedSignal = _startedSignals.GetOrAdd(task.Id, _ => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));
            startedSignal.TrySetResult(true);
            OnTaskStarted?.Invoke(task);

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                OnTaskCompleted?.Invoke(task, SyncTaskResult.Success);
                return SyncTaskResult.Success;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                OnTaskCompleted?.Invoke(task, SyncTaskResult.Cancelled);
                return SyncTaskResult.Cancelled;
            }
        }

        public Task<IReadOnlyList<SyncTaskResult>> ExecuteBatchAsync(IEnumerable<ISyncTask> tasks, SyncTaskExecutionOptions options, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task QueueTaskAsync(ISyncTask task)
        {
            return Task.CompletedTask;
        }

        public Task PauseExecution()
        {
            return Task.CompletedTask;
        }

        public Task ResumeExecution()
        {
            return Task.CompletedTask;
        }

        public async Task WaitForTaskStartedAsync(string taskId)
        {
            var signal = _startedSignals.GetOrAdd(taskId, _ => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));
            await signal.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    private sealed class TestConfigurationManagementService : IConfigurationManagementService
    {
        private readonly SyncFrameworkOptions _syncOptions = new()
        {
            MaxConcurrentTasks = 1,
            EnableSyncFramework = true,
            SchedulerPollingIntervalSeconds = 1
        };

        public string ConfigurationFilePath => Path.Combine(Path.GetTempPath(), "unused.yaml");

        public string GenerateDefaultYaml()
        {
            throw new NotSupportedException();
        }

        public ServiceOptions GetServiceOptions()
        {
            throw new NotSupportedException();
        }

        public LoggingOptions GetLoggingOptions()
        {
            throw new NotSupportedException();
        }

        public InterfaceOptions GetInterfaceOptions()
        {
            throw new NotSupportedException();
        }

        public PluginSystemOptions GetPluginSystemOptions()
        {
            throw new NotSupportedException();
        }

        public SyncFrameworkOptions GetSyncOptions()
        {
            return _syncOptions;
        }

        public AppConfigurationDocument GetCurrentConfiguration()
        {
            throw new NotSupportedException();
        }

        public Task EnsureDefaultConfigurationFileAsync(CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<AppConfigurationDocument> LoadAsync(CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task SaveAsync(AppConfigurationDocument configuration, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class TestSyncTaskGenerator(IReadOnlyList<ISyncTask> tasks) : ISyncTaskGenerator
    {
        public Task<IReadOnlyList<ISyncTask>> GenerateTasksAsync(SyncPlan plan)
        {
            return Task.FromResult<IReadOnlyList<ISyncTask>>(tasks.ToList());
        }

        public Task<(bool IsValid, IReadOnlyList<string> Errors)> ValidatePlanAsync(SyncPlan plan)
        {
            return Task.FromResult<(bool IsValid, IReadOnlyList<string> Errors)>((true, []));
        }
    }

    private sealed class ProgressReportingTask(string id, string planId) : ISyncTask
    {
        private int _progressSequence;

        public string Id { get; } = id;

        public string PlanId { get; } = planId;

        public NodeConfiguration MasterNode { get; } = CreateNodeConfiguration("master", "Local");

        public NodeConfiguration SlaveNode { get; } = CreateNodeConfiguration("slave", "Local");

        public SyncMode SyncMode => SyncMode.Bidirectional;

        public string SyncItemType => "FileSystem";

        public string? SourcePath => null;

        public string? TargetPath => null;

        public ConflictResolutionStrategy ConflictResolutionStrategy => ConflictResolutionStrategy.Manual;

        public SyncTaskState State => SyncTaskState.Pending;

        public SyncTaskResult? Result => null;

        public ISyncTaskProgress? Progress => null;

        public DateTimeOffset? StartTime => null;

        public DateTimeOffset? CompletionTime => null;

        public IReadOnlyList<string>? Errors => null;

        public TaskCompletionSource<bool> Started { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> AllowCompletion { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public event Action<ISyncTask, SyncTaskState>? OnStateChanged;

        public event Action<ISyncTask, ISyncTaskProgress>? OnProgressChanged;

        public event Action<ISyncTask, SyncTaskResult>? OnCompleted;

        public void ResetForNextExecution()
        {
            Started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            AllowCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public void ReportProgress(int processedFiles, int totalFiles, long transferredBytes, long totalBytes, string? currentFilePath, string currentOperation)
        {
        }

        public void AddError(string error)
        {
        }

        public async Task<SyncTaskResult> ExecuteAsync(CancellationToken cancellationToken)
        {
            Started.TrySetResult(true);
            var progress = new TestSyncTaskProgress(Interlocked.Increment(ref _progressSequence));
            OnProgressChanged?.Invoke(this, progress);
            await AllowCompletion.Task.WaitAsync(cancellationToken);
            OnCompleted?.Invoke(this, SyncTaskResult.Success);
            return SyncTaskResult.Success;
        }

        public Task PauseAsync()
        {
            return Task.CompletedTask;
        }

        public Task ResumeAsync()
        {
            return Task.CompletedTask;
        }

        public Task CancelAsync()
        {
            return Task.CompletedTask;
        }
    }

    private sealed class TestSyncTaskProgress : ISyncTaskProgress
    {
        private readonly int _processedFiles;

        public TestSyncTaskProgress(int processedFiles)
        {
            _processedFiles = processedFiles;
        }

        public int ProcessedFiles => _processedFiles;

        public int TotalFiles => 2;

        public long TransferredBytes => _processedFiles;

        public long TotalBytes => 2;

        public string? CurrentFilePath => "test.txt";

        public string CurrentOperation => "Testing";

        public DateTimeOffset StartTime { get; } = DateTimeOffset.UtcNow;

        public TimeSpan? EstimatedRemainingTime => TimeSpan.Zero;

        public double TransferSpeedBytesPerSecond => 1;
    }
}

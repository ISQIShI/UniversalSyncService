using Microsoft.Extensions.Logging;
using UniversalSyncService.Abstractions.SyncManagement.Engine;

namespace UniversalSyncService.Core.SyncManagement.Engine;

public sealed class ConflictResolver : IConflictResolver
{
    private readonly ILogger<ConflictResolver> _logger;

    public ConflictResolver(ILogger<ConflictResolver> logger)
    {
        _logger = logger;
        DefaultStrategy = ConflictResolutionStrategy.Manual;
    }

    public ConflictResolutionStrategy DefaultStrategy { get; set; }

    public event Action<ISyncConflict>? OnConflictDetected;

    public event Action<ISyncConflict, SyncDecision>? OnConflictResolved;

    public Task<SyncDecision> ResolveAsync(ISyncConflict conflict, ConflictResolutionStrategy? strategy = null)
    {
        ArgumentNullException.ThrowIfNull(conflict);

        OnConflictDetected?.Invoke(conflict);

        var actualStrategy = strategy ?? DefaultStrategy;
        var decision = actualStrategy switch
        {
            ConflictResolutionStrategy.KeepLocal => SyncDecision.Push,
            ConflictResolutionStrategy.KeepRemote => SyncDecision.Pull,
            ConflictResolutionStrategy.KeepNewer => ResolveByModifiedAt(conflict),
            ConflictResolutionStrategy.KeepLarger when conflict.MasterState is not null && conflict.SlaveState is not null && conflict.MasterState.Size > conflict.SlaveState.Size
                => SyncDecision.Push,
            ConflictResolutionStrategy.KeepLarger when conflict.MasterState is not null && conflict.SlaveState is not null && conflict.SlaveState.Size > conflict.MasterState.Size
                => SyncDecision.Pull,
            ConflictResolutionStrategy.KeepLarger => SyncDecision.Conflict,
            ConflictResolutionStrategy.RenameBoth => SyncDecision.ConflictRename,
            _ => SyncDecision.Conflict
        };

        _logger.LogInformation(
            "已根据策略 {Strategy} 解决冲突。文件={FilePath}，决策={Decision}",
            actualStrategy,
            conflict.FilePath,
            decision);

        OnConflictResolved?.Invoke(conflict, decision);
        return Task.FromResult(decision);
    }

    public async Task<Dictionary<string, SyncDecision>> ResolveBatchAsync(
        IEnumerable<ISyncConflict> conflicts,
        ConflictResolutionStrategy strategy)
    {
        ArgumentNullException.ThrowIfNull(conflicts);

        var results = new Dictionary<string, SyncDecision>(StringComparer.OrdinalIgnoreCase);
        foreach (var conflict in conflicts)
        {
            var decision = await ResolveAsync(conflict, strategy);
            results[conflict.FilePath] = decision;
        }

        return results;
    }

    private static SyncDecision ResolveByModifiedAt(ISyncConflict conflict)
    {
        var localModifiedAt = conflict.MasterState?.ModifiedAt;
        var remoteModifiedAt = conflict.SlaveState?.ModifiedAt;
        if (localModifiedAt.HasValue && remoteModifiedAt.HasValue)
        {
            if (localModifiedAt > remoteModifiedAt)
            {
                return SyncDecision.Push;
            }

            if (remoteModifiedAt > localModifiedAt)
            {
                return SyncDecision.Pull;
            }

            return SyncDecision.Conflict;
        }

        return SyncDecision.Conflict;
    }
}

using UniversalSyncService.Abstractions.SyncItems;
using UniversalSyncService.Abstractions.SyncManagement.Engine;

namespace UniversalSyncService.Core.SyncManagement.Engine;

public sealed class SyncConflict : ISyncConflict
{
    public string FilePath { get; }

    public IFileStateSnapshot? MasterState { get; }

    public IFileStateSnapshot? SlaveState { get; }

    public IFileStateSnapshot? MasterHistoryState { get; }

    public IFileStateSnapshot? SlaveHistoryState { get; }

    public DateTimeOffset DetectedAt { get; }

    public string Description { get; }

    public SyncConflict(
        string filePath,
        IFileStateSnapshot? masterState,
        IFileStateSnapshot? slaveState,
        IFileStateSnapshot? masterHistoryState,
        IFileStateSnapshot? slaveHistoryState,
        string description)
    {
        FilePath = filePath;
        MasterState = masterState;
        SlaveState = slaveState;
        MasterHistoryState = masterHistoryState;
        SlaveHistoryState = slaveHistoryState;
        Description = description;
        DetectedAt = DateTimeOffset.Now;
    }
}

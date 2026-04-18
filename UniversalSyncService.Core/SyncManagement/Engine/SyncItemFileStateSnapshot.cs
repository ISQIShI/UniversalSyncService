using UniversalSyncService.Abstractions.SyncItems;

namespace UniversalSyncService.Core.SyncManagement.Engine;

public sealed class SyncItemFileStateSnapshot : IFileStateSnapshot
{
    public string Path { get; }

    public long Size { get; }

    public DateTimeOffset? ModifiedAt { get; }

    public string? Checksum { get; }

    public SyncItemFileStateSnapshot(string path, long size, DateTimeOffset? modifiedAt, string? checksum)
    {
        Path = path;
        Size = size;
        ModifiedAt = modifiedAt;
        Checksum = checksum;
    }

    public static SyncItemFileStateSnapshot FromMetadata(SyncItemMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return new SyncItemFileStateSnapshot(metadata.Path, metadata.Size, metadata.ModifiedAt, metadata.Checksum);
    }
}

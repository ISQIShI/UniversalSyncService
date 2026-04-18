using UniversalSyncService.Abstractions.SyncManagement.Tasks;

namespace UniversalSyncService.Core.SyncManagement.Tasks;

public sealed class SyncTaskProgress : ISyncTaskProgress
{
    public int ProcessedFiles { get; private set; }

    public int TotalFiles { get; private set; }

    public long TransferredBytes { get; private set; }

    public long TotalBytes { get; private set; }

    public string? CurrentFilePath { get; private set; }

    public string CurrentOperation { get; private set; }

    public DateTimeOffset StartTime { get; }

    public TimeSpan? EstimatedRemainingTime { get; private set; }

    public double TransferSpeedBytesPerSecond { get; private set; }

    public SyncTaskProgress(DateTimeOffset startTime, string currentOperation)
    {
        StartTime = startTime;
        CurrentOperation = currentOperation;
    }

    public void Update(
        int processedFiles,
        int totalFiles,
        long transferredBytes,
        long totalBytes,
        string? currentFilePath,
        string currentOperation)
    {
        ProcessedFiles = processedFiles;
        TotalFiles = totalFiles;
        TransferredBytes = transferredBytes;
        TotalBytes = totalBytes;
        CurrentFilePath = currentFilePath;
        CurrentOperation = currentOperation;

        var elapsed = DateTimeOffset.Now - StartTime;
        TransferSpeedBytesPerSecond = elapsed.TotalSeconds <= 0
            ? 0
            : transferredBytes / elapsed.TotalSeconds;

        EstimatedRemainingTime = TransferSpeedBytesPerSecond <= 0 || totalBytes <= transferredBytes
            ? null
            : TimeSpan.FromSeconds((totalBytes - transferredBytes) / TransferSpeedBytesPerSecond);
    }
}

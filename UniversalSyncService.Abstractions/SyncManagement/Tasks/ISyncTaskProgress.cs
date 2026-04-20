namespace UniversalSyncService.Abstractions.SyncManagement.Tasks;

/// <summary>
/// 表示同步任务的执行进度。
/// </summary>
public interface ISyncTaskProgress
{
    /// <summary>
    /// 获取已处理的文件数。
    /// </summary>
    int ProcessedFiles { get; }

    /// <summary>
    /// 获取总文件数。
    /// </summary>
    int TotalFiles { get; }

    /// <summary>
    /// 获取已传输的字节数。
    /// </summary>
    long TransferredBytes { get; }

    /// <summary>
    /// 获取总字节数。
    /// </summary>
    long TotalBytes { get; }

    /// <summary>
    /// 获取当前处理的文件路径。
    /// </summary>
    string? CurrentFilePath { get; }

    /// <summary>
    /// 获取当前操作（如"扫描中"、"上传中"、"下载中"等）。
    /// </summary>
    string CurrentOperation { get; }

    /// <summary>
    /// 获取任务开始时间。
    /// </summary>
    DateTimeOffset StartTime { get; }

    /// <summary>
    /// 获取预计剩余时间。
    /// </summary>
    TimeSpan? EstimatedRemainingTime { get; }

    /// <summary>
    /// 获取传输速度（字节/秒）。
    /// </summary>
    double TransferSpeedBytesPerSecond { get; }
}

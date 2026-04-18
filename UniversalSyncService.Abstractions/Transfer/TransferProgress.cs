namespace UniversalSyncService.Abstractions.Transfer;

/// <summary>
/// 表示传输进度信息。
/// </summary>
public readonly record struct TransferProgress
{
    /// <summary>
    /// 已传输字节数。
    /// </summary>
    public long BytesTransferred { get; }

    /// <summary>
    /// 总字节数。
    /// </summary>
    public long TotalBytes { get; }

    /// <summary>
    /// 已完成百分比。
    /// </summary>
    public double Percentage { get; }

    /// <summary>
    /// 已用时间。
    /// </summary>
    public TimeSpan Elapsed { get; }

    /// <summary>
    /// 预计剩余时间。
    /// </summary>
    public TimeSpan? EstimatedRemaining { get; }

    /// <summary>
    /// 每秒传输字节数。
    /// </summary>
    public double BytesPerSecond { get; }

    /// <summary>
    /// 初始化传输进度信息。
    /// </summary>
    public TransferProgress(
        long bytesTransferred,
        long totalBytes,
        double percentage,
        TimeSpan elapsed,
        TimeSpan? estimatedRemaining,
        double bytesPerSecond)
    {
        BytesTransferred = bytesTransferred;
        TotalBytes = totalBytes;
        Percentage = percentage;
        Elapsed = elapsed;
        EstimatedRemaining = estimatedRemaining;
        BytesPerSecond = bytesPerSecond;
    }
}

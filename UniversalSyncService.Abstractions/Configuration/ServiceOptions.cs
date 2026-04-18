namespace UniversalSyncService.Abstractions.Configuration;

public sealed class ServiceOptions
{
    public const string SectionName = "UniversalSyncService:Service";

    [ConfigComment("服务名称，用于日志和状态展示。")]
    public string ServiceName { get; set; } = "UniversalSyncService.Host";

    [ConfigComment("心跳日志输出间隔（秒）。")]
    public int HeartbeatIntervalSeconds { get; set; } = 10;
}

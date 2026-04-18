namespace UniversalSyncService.Abstractions.Configuration;

public sealed class LoggingOptions
{
    public const string SectionName = "UniversalSyncService:Logging";

    [ConfigComment("全局最小日志级别。")]
    public string MinimumLevel { get; set; } = "Information";

    [ConfigComment("是否启用控制台日志输出。")]
    public bool EnableConsoleSink { get; set; } = true;

    [ConfigComment("是否启用文件日志输出。")]
    public bool EnableFileSink { get; set; } = true;

    [ConfigComment("按命名空间覆盖日志级别，例如 Microsoft=Warning。")]
    public Dictionary<string, string> Overrides { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Microsoft"] = "Warning",
        ["System"] = "Warning"
    };

    [ConfigComment("文件日志输出配置。")]
    public FileSinkOptions File { get; set; } = new();
}

public sealed class FileSinkOptions
{
    [ConfigComment("日志文件路径，支持 Serilog Rolling 文件名模板。")]
    public string Path { get; set; } = "logs/universal-sync-.log";

    [ConfigComment("滚动周期，可选 Infinite/Year/Month/Day/Hour/Minute。")]
    public string RollingInterval { get; set; } = "Day";

    [ConfigComment("保留文件数量上限，null 表示不限制。")]
    public int? RetainedFileCountLimit { get; set; } = 14;

    [ConfigComment("单文件大小上限（字节），null 表示不限制。")]
    public long? FileSizeLimitBytes { get; set; } = 10485760;

    [ConfigComment("达到文件大小上限时是否滚动新文件。")]
    public bool RollOnFileSizeLimit { get; set; } = true;

    [ConfigComment("日志输出模板。")]
    public string OutputTemplate { get; set; } =
        "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] ({SourceContext}) {Message:lj}{NewLine}{Exception}";
}

using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Events;
using UniversalSyncService.Abstractions.Configuration;

namespace UniversalSyncService.Host.Logging;

public static class SerilogRegistrationExtensions
{
    public static IServiceCollection AddUniversalSyncSerilog(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // 统一通过配置管理器读取日志配置，避免出现多套读取路径。
        services.AddSerilog(
            (serviceProvider, loggerConfiguration) =>
            {
                var configurationManager = serviceProvider.GetRequiredService<IConfigurationManagementService>();
                var options = configurationManager.GetLoggingOptions();
                ConfigureLogger(loggerConfiguration, options);
            },
            preserveStaticLogger: false,
            writeToProviders: false);

        return services;
    }

    private static void ConfigureLogger(LoggerConfiguration loggerConfiguration, LoggingOptions options)
    {
        loggerConfiguration
            .Enrich.FromLogContext()
            .MinimumLevel.Is(ParseLogLevel(options.MinimumLevel));

        foreach (var (source, level) in options.Overrides)
        {
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(level))
            {
                continue;
            }

            loggerConfiguration.MinimumLevel.Override(source, ParseLogLevel(level));
        }

        if (options.EnableConsoleSink)
        {
            // 控制台输出用于本地开发与容器标准输出采集。
            loggerConfiguration.WriteTo.Console();
        }

        if (options.EnableFileSink)
        {
            // 文件输出用于落地持久化日志，便于问题追溯。
            loggerConfiguration.WriteTo.File(
                path: options.File.Path,
                rollingInterval: ParseRollingInterval(options.File.RollingInterval),
                retainedFileCountLimit: options.File.RetainedFileCountLimit,
                fileSizeLimitBytes: options.File.FileSizeLimitBytes,
                rollOnFileSizeLimit: options.File.RollOnFileSizeLimit,
                outputTemplate: options.File.OutputTemplate);
        }
    }

    private static LogEventLevel ParseLogLevel(string value)
    {
        return Enum.TryParse<LogEventLevel>(value, ignoreCase: true, out var level)
            ? level
            : LogEventLevel.Information;
    }

    private static RollingInterval ParseRollingInterval(string value)
    {
        return Enum.TryParse<RollingInterval>(value, ignoreCase: true, out var rollingInterval)
            ? rollingInterval
            : RollingInterval.Day;
    }
}

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using UniversalSyncService.Abstractions.Configuration;

namespace UniversalSyncService.Host.Configuration;

public sealed class ConfigurationManagementService : IConfigurationManagementService
{
    private readonly IHostEnvironment _hostEnvironment;
    private readonly IOptionsMonitor<ServiceOptions> _serviceOptionsMonitor;
    private readonly IOptionsMonitor<LoggingOptions> _loggingOptionsMonitor;
    private readonly IOptionsMonitor<InterfaceOptions> _interfaceOptionsMonitor;
    private readonly IOptionsMonitor<PluginSystemOptions> _pluginSystemOptionsMonitor;
    private readonly IOptionsMonitor<SyncFrameworkOptions> _syncOptionsMonitor;
    private readonly SemaphoreSlim _ioLock = new(1, 1);

    public ConfigurationManagementService(
        IHostEnvironment hostEnvironment,
        IOptionsMonitor<ServiceOptions> serviceOptionsMonitor,
        IOptionsMonitor<LoggingOptions> loggingOptionsMonitor,
        IOptionsMonitor<InterfaceOptions> interfaceOptionsMonitor,
        IOptionsMonitor<PluginSystemOptions> pluginSystemOptionsMonitor,
        IOptionsMonitor<SyncFrameworkOptions> syncOptionsMonitor)
    {
        _hostEnvironment = hostEnvironment;
        _serviceOptionsMonitor = serviceOptionsMonitor;
        _loggingOptionsMonitor = loggingOptionsMonitor;
        _interfaceOptionsMonitor = interfaceOptionsMonitor;
        _pluginSystemOptionsMonitor = pluginSystemOptionsMonitor;
        _syncOptionsMonitor = syncOptionsMonitor;
    }

    public string ConfigurationFilePath =>
        Path.Combine(_hostEnvironment.ContentRootPath, "appsettings.yaml");

    public string GenerateDefaultYaml()
    {
        return DefaultConfigurationYamlGenerator.GenerateDefaultConfigurationYaml();
    }

    public ServiceOptions GetServiceOptions()
    {
        return CloneServiceOptions(_serviceOptionsMonitor.CurrentValue);
    }

    public LoggingOptions GetLoggingOptions()
    {
        return CloneLoggingOptions(_loggingOptionsMonitor.CurrentValue);
    }

    public InterfaceOptions GetInterfaceOptions()
    {
        return CloneInterfaceOptions(_interfaceOptionsMonitor.CurrentValue);
    }

    public PluginSystemOptions GetPluginSystemOptions()
    {
        return ClonePluginSystemOptions(_pluginSystemOptionsMonitor.CurrentValue);
    }

    public SyncFrameworkOptions GetSyncOptions()
    {
        return CloneSyncFrameworkOptions(_syncOptionsMonitor.CurrentValue);
    }

    public AppConfigurationDocument GetCurrentConfiguration()
    {
        return new AppConfigurationDocument
        {
            UniversalSyncService = new UniversalSyncServiceConfiguration
            {
                Service = GetServiceOptions(),
                Logging = GetLoggingOptions(),
                Interface = GetInterfaceOptions(),
                Plugins = GetPluginSystemOptions(),
                Sync = GetSyncOptions()
            }
        };
    }

    public async Task EnsureDefaultConfigurationFileAsync(CancellationToken cancellationToken = default)
    {
        if (File.Exists(ConfigurationFilePath))
        {
            return;
        }

        // 首次启动时自动生成默认配置文件，便于后续在 UI 中编辑。
        var defaultYaml = GenerateDefaultYaml();
        await _ioLock.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(ConfigurationFilePath))
            {
                return;
            }

            var directory = Path.GetDirectoryName(ConfigurationFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(ConfigurationFilePath, defaultYaml, cancellationToken);
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public async Task<AppConfigurationDocument> LoadAsync(CancellationToken cancellationToken = default)
    {
        // 读取逻辑统一走 IOptionsMonitor 当前快照，避免出现“文件解析值”和“运行时绑定值”不一致。
        await EnsureDefaultConfigurationFileAsync(cancellationToken);
        return GetCurrentConfiguration();
    }

    public async Task SaveAsync(AppConfigurationDocument configuration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // 保存时同步输出注释，方便用户在 UI 和文件中理解配置含义。
        var yamlText = DefaultConfigurationYamlGenerator.GenerateYaml(configuration);

        await _ioLock.WaitAsync(cancellationToken);
        try
        {
            await File.WriteAllTextAsync(ConfigurationFilePath, yamlText, cancellationToken);
        }
        finally
        {
            _ioLock.Release();
        }
    }

    private static ServiceOptions CloneServiceOptions(ServiceOptions source)
    {
        return new ServiceOptions
        {
            ServiceName = source.ServiceName,
            HeartbeatIntervalSeconds = source.HeartbeatIntervalSeconds
        };
    }

    private static InterfaceOptions CloneInterfaceOptions(InterfaceOptions source)
    {
        return new InterfaceOptions
        {
            EnableGrpc = source.EnableGrpc,
            EnableHttpApi = source.EnableHttpApi,
            EnableWebConsole = source.EnableWebConsole,
            RequireManagementApiKey = source.RequireManagementApiKey,
            AllowAnonymousLoopback = source.AllowAnonymousLoopback,
            ManagementApiKey = source.ManagementApiKey
        };
    }

    private static LoggingOptions CloneLoggingOptions(LoggingOptions source)
    {
        return new LoggingOptions
        {
            MinimumLevel = source.MinimumLevel,
            EnableConsoleSink = source.EnableConsoleSink,
            EnableFileSink = source.EnableFileSink,
            Overrides = new Dictionary<string, string>(source.Overrides, StringComparer.OrdinalIgnoreCase),
            File = CloneFileSinkOptions(source.File)
        };
    }

    private static FileSinkOptions CloneFileSinkOptions(FileSinkOptions source)
    {
        return new FileSinkOptions
        {
            Path = source.Path,
            RollingInterval = source.RollingInterval,
            RetainedFileCountLimit = source.RetainedFileCountLimit,
            FileSizeLimitBytes = source.FileSizeLimitBytes,
            RollOnFileSizeLimit = source.RollOnFileSizeLimit,
            OutputTemplate = source.OutputTemplate
        };
    }

    private static PluginSystemOptions ClonePluginSystemOptions(PluginSystemOptions source)
    {
        return new PluginSystemOptions
        {
            EnablePluginSystem = source.EnablePluginSystem,
            PluginDirectory = source.PluginDirectory,
            Descriptors = source.Descriptors
                .Select(descriptor => new PluginDescriptorOptions
                {
                    Id = descriptor.Id,
                    Enabled = descriptor.Enabled,
                    AssemblyPath = descriptor.AssemblyPath,
                    EntryType = descriptor.EntryType,
                    Description = descriptor.Description
                })
                .ToList()
        };
    }

    private static SyncFrameworkOptions CloneSyncFrameworkOptions(SyncFrameworkOptions source)
    {
        return new SyncFrameworkOptions
        {
            EnableSyncFramework = source.EnableSyncFramework,
            SchedulerPollingIntervalSeconds = source.SchedulerPollingIntervalSeconds,
            MaxConcurrentTasks = source.MaxConcurrentTasks,
            HistoryRetentionVersions = source.HistoryRetentionVersions,
            HistoryStorePath = source.HistoryStorePath,
            Nodes = source.Nodes
                .Select(node => new ConfiguredNodeOptions
                {
                    Id = node.Id,
                    Name = node.Name,
                    NodeType = node.NodeType,
                    ConnectionSettings = new Dictionary<string, string>(node.ConnectionSettings, StringComparer.OrdinalIgnoreCase),
                    CustomOptions = new Dictionary<string, string>(node.CustomOptions, StringComparer.OrdinalIgnoreCase),
                    CreatedAt = node.CreatedAt,
                    ModifiedAt = node.ModifiedAt,
                    IsEnabled = node.IsEnabled
                })
                .ToList(),
            Plans = source.Plans
                .Select(plan => new SyncPlanOptions
                {
                    Id = plan.Id,
                    Name = plan.Name,
                    Description = plan.Description,
                    MasterNodeId = plan.MasterNodeId,
                    SyncItemType = plan.SyncItemType,
                    SlaveConfigurations = plan.SlaveConfigurations
                        .Select(slave => new SyncPlanSlaveConfigurationOptions
                        {
                            SlaveNodeId = slave.SlaveNodeId,
                            SyncMode = slave.SyncMode,
                            SourcePath = slave.SourcePath,
                            TargetPath = slave.TargetPath,
                            EnableDeletionProtection = slave.EnableDeletionProtection,
                            ConflictResolutionStrategy = slave.ConflictResolutionStrategy,
                            Filters = [.. slave.Filters],
                            Exclusions = [.. slave.Exclusions]
                        })
                        .ToList(),
                    Schedule = new SyncScheduleOptions
                    {
                        TriggerType = plan.Schedule.TriggerType,
                        CronExpression = plan.Schedule.CronExpression,
                        Interval = plan.Schedule.Interval,
                        NextScheduledTime = plan.Schedule.NextScheduledTime,
                        EnableFileSystemWatcher = plan.Schedule.EnableFileSystemWatcher
                    },
                    IsEnabled = plan.IsEnabled,
                    CreatedAt = plan.CreatedAt,
                    ModifiedAt = plan.ModifiedAt,
                    LastExecutedAt = plan.LastExecutedAt,
                    ExecutionCount = plan.ExecutionCount
                })
                .ToList()
        };
    }
}

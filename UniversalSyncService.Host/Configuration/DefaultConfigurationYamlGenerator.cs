using UniversalSyncService.Abstractions.Configuration;

namespace UniversalSyncService.Host.Configuration;

public static class DefaultConfigurationYamlGenerator
{
    public static string GenerateDefaultConfigurationYaml()
    {
        var root = CreateDefaultConfigurationDocument();
        return ConfigurationYamlCommentSerializer.Serialize(root);
    }

    public static string GenerateYaml(AppConfigurationDocument configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return ConfigurationYamlCommentSerializer.Serialize(configuration);
    }

    public static void EnsureDefaultConfigurationFile(string configurationFilePath)
    {
        if (File.Exists(configurationFilePath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(configurationFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // 首次启动自动落地默认配置，方便 UI 或用户后续编辑。
        var defaultYaml = GenerateDefaultConfigurationYaml();
        File.WriteAllText(configurationFilePath, defaultYaml);
    }

    private static AppConfigurationDocument CreateDefaultConfigurationDocument()
    {
        // 默认配置直接附带一组本地节点与文件系统计划样例，便于后续做功能验证。
        return new AppConfigurationDocument
        {
            UniversalSyncService = new UniversalSyncServiceConfiguration
            {
                Interface = new InterfaceOptions
                {
                    RequireManagementApiKey = false,
                    AllowAnonymousLoopback = true,
                    ManagementApiKey = "change-me"
                },
                Sync = new SyncFrameworkOptions
                {
                    HostWorkspacePath = "sync-test/master",
                    HistoryStorePath = "data/sync-history.db",
                    Nodes =
                    [
                        new ConfiguredNodeOptions
                        {
                            Id = "local-slave",
                            Name = "本地从节点",
                            NodeType = "Local",
                            ConnectionSettings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                            {
                                ["RootPath"] = "sync-test/slave"
                            }
                        }
                    ],
                    Plans =
                    [
                        new SyncPlanOptions
                        {
                            Id = "local-filesystem-test",
                            Name = "本地文件系统测试计划",
                            Description = "用于本地节点与普通文件系统同步对象的功能测试。",
                            MasterNodeId = string.Empty,
                            SyncItemType = "FileSystem",
                            IsEnabled = false,
                            Schedule = new SyncScheduleOptions
                            {
                                TriggerType = UniversalSyncService.Abstractions.SyncManagement.Plans.SyncTriggerType.Manual,
                                EnableFileSystemWatcher = false
                            },
                            SlaveConfigurations =
                            [
                                new SyncPlanSlaveConfigurationOptions
                                {
                                    SlaveNodeId = "local-slave",
                                    SyncMode = UniversalSyncService.Abstractions.SyncManagement.Plans.SyncMode.Bidirectional,
                                    SourcePath = ".",
                                    TargetPath = ".",
                                    EnableDeletionProtection = true,
                                    ConflictResolutionStrategy = UniversalSyncService.Abstractions.SyncManagement.Engine.ConflictResolutionStrategy.Manual
                                }
                            ]
                        }
                    ]
                }
            }
        };
    }
}

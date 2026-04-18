using UniversalSyncService.Abstractions.Configuration;
using UniversalSyncService.Abstractions.SyncManagement.ConfigNodes;
using UniversalSyncService.Abstractions.SyncManagement.Plans;

namespace UniversalSyncService.Core.SyncManagement;

internal static class SyncConfigurationMapper
{
    public static NodeConfiguration ToNodeConfiguration(ConfiguredNodeOptions source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var node = new NodeConfiguration(
            source.Id,
            source.Name,
            source.NodeType,
            new Dictionary<string, string>(source.ConnectionSettings, StringComparer.OrdinalIgnoreCase),
            source.CreatedAt)
        {
            ModifiedAt = source.ModifiedAt,
            IsEnabled = source.IsEnabled,
            CustomOptions = source.CustomOptions.ToDictionary(
                pair => pair.Key,
                pair => (object)pair.Value,
                StringComparer.OrdinalIgnoreCase)
        };

        return node;
    }

    public static ConfiguredNodeOptions ToNodeOptions(NodeConfiguration source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new ConfiguredNodeOptions
        {
            Id = source.Id,
            Name = source.Name,
            NodeType = source.NodeType,
            ConnectionSettings = new Dictionary<string, string>(source.ConnectionSettings, StringComparer.OrdinalIgnoreCase),
            CustomOptions = (source.CustomOptions ?? new Dictionary<string, object>())
                .ToDictionary(
                    pair => pair.Key,
                    pair => Convert.ToString(pair.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                    StringComparer.OrdinalIgnoreCase),
            CreatedAt = source.CreatedAt,
            ModifiedAt = source.ModifiedAt,
            IsEnabled = source.IsEnabled
        };
    }

    public static SyncPlan ToSyncPlan(SyncPlanOptions source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var plan = new SyncPlan(
            source.Id,
            source.Name,
            source.MasterNodeId,
            source.SyncItemType,
            source.SlaveConfigurations.Select(ToSlaveConfiguration).ToList(),
            ToSchedule(source.Schedule),
            source.CreatedAt)
        {
            Description = source.Description,
            IsEnabled = source.IsEnabled,
            ModifiedAt = source.ModifiedAt,
            LastExecutedAt = source.LastExecutedAt,
            ExecutionCount = source.ExecutionCount
        };

        return plan;
    }

    public static SyncPlanOptions ToSyncPlanOptions(SyncPlan source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new SyncPlanOptions
        {
            Id = source.Id,
            Name = source.Name,
            Description = source.Description,
            MasterNodeId = source.MasterNodeId,
            SyncItemType = source.SyncItemType,
            SlaveConfigurations = source.SlaveConfigurations.Select(ToSlaveConfigurationOptions).ToList(),
            Schedule = ToScheduleOptions(source.Schedule),
            IsEnabled = source.IsEnabled,
            CreatedAt = source.CreatedAt,
            ModifiedAt = source.ModifiedAt,
            LastExecutedAt = source.LastExecutedAt,
            ExecutionCount = source.ExecutionCount
        };
    }

    private static SyncPlanSlaveConfiguration ToSlaveConfiguration(SyncPlanSlaveConfigurationOptions source)
    {
        var configuration = new SyncPlanSlaveConfiguration(source.SlaveNodeId, source.SyncMode)
        {
            SourcePath = source.SourcePath,
            TargetPath = source.TargetPath,
            EnableDeletionProtection = source.EnableDeletionProtection,
            ConflictResolutionStrategy = source.ConflictResolutionStrategy,
            Filters = [.. source.Filters],
            Exclusions = [.. source.Exclusions]
        };

        return configuration;
    }

    private static SyncPlanSlaveConfigurationOptions ToSlaveConfigurationOptions(SyncPlanSlaveConfiguration source)
    {
        return new SyncPlanSlaveConfigurationOptions
        {
            SlaveNodeId = source.SlaveNodeId,
            SyncMode = source.SyncMode,
            SourcePath = source.SourcePath,
            TargetPath = source.TargetPath,
            EnableDeletionProtection = source.EnableDeletionProtection,
            ConflictResolutionStrategy = source.ConflictResolutionStrategy,
            Filters = source.Filters is null ? [] : [.. source.Filters],
            Exclusions = source.Exclusions is null ? [] : [.. source.Exclusions]
        };
    }

    private static SyncSchedule ToSchedule(SyncScheduleOptions source)
    {
        var schedule = new SyncSchedule(source.TriggerType)
        {
            CronExpression = source.CronExpression,
            Interval = source.Interval,
            NextScheduledTime = source.NextScheduledTime,
            EnableFileSystemWatcher = source.EnableFileSystemWatcher
        };

        return schedule;
    }

    private static SyncScheduleOptions ToScheduleOptions(SyncSchedule source)
    {
        return new SyncScheduleOptions
        {
            TriggerType = source.TriggerType,
            CronExpression = source.CronExpression,
            Interval = source.Interval,
            NextScheduledTime = source.NextScheduledTime,
            EnableFileSystemWatcher = source.EnableFileSystemWatcher
        };
    }
}

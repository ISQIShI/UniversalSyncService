using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using UniversalSyncService.Abstractions.Configuration;
using UniversalSyncService.Abstractions.SyncManagement;
using UniversalSyncService.Abstractions.SyncManagement.Plans;
using UniversalSyncService.Abstractions.SyncManagement.Tasks;
using UniversalSyncService.Core.SyncManagement;
using UniversalSyncService.Core.SyncManagement.ConfigNodes;
using Xunit;

namespace UniversalSyncService.Host.IntegrationTests;

public sealed class SyncCoordinatorHostedServiceRegressionTests : IAsyncLifetime
{
    private string _contentRootPath = string.Empty;

    public Task InitializeAsync()
    {
        _contentRootPath = Path.Combine(Path.GetTempPath(), "UniversalSyncService-CoordinatorTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_contentRootPath);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(_contentRootPath))
        {
            Directory.Delete(_contentRootPath, recursive: true);
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task Coordinator_ShouldSkipPollingExecution_WhenFrameworkDisabledUntilEnabled()
    {
        // Arrange
        var duePlan = new SyncPlan(
            "plan-due",
            "计划",
            "host-local",
            "FileSystem",
            [new SyncPlanSlaveConfiguration("slave", SyncMode.Bidirectional)],
            new SyncSchedule(SyncTriggerType.Scheduled)
            {
                Interval = TimeSpan.FromSeconds(1),
                NextScheduledTime = DateTimeOffset.UtcNow.AddSeconds(-1)
            })
        {
            IsEnabled = true
        };

        var configService = new TestConfigurationManagementService(new SyncFrameworkOptions
        {
            EnableSyncFramework = false,
            SchedulerPollingIntervalSeconds = 1,
            HistoryStorePath = "data/sync-history.db",
            HostWorkspacePath = "."
        });
        var nodeRegistry = new NodeRegistry();
        var planManager = new TestSyncPlanManager([duePlan]);
        var service = new SyncCoordinatorHostedService(
            configService,
            new TestHostEnvironment(_contentRootPath),
            nodeRegistry,
            planManager,
            NullLogger<SyncCoordinatorHostedService>.Instance);

        // Act
        await service.StartAsync(CancellationToken.None);
        try
        {
            await configService.WaitForGetSyncOptionsCallCountAsync(2);
            Assert.Equal(0, planManager.ExecutePlanNowCallCount);

            configService.SetFrameworkEnabled(true);
            await planManager.WaitForExecutePlanNowAsync(TimeSpan.FromSeconds(4));
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        // Assert
        Assert.True(planManager.ExecutePlanNowCallCount >= 1);
    }

    [Fact]
    public async Task Coordinator_ShouldRegisterHostLocalNode_WithHistoryDatabaseExclusions()
    {
        // Arrange
        var configService = new TestConfigurationManagementService(new SyncFrameworkOptions
        {
            EnableSyncFramework = false,
            SchedulerPollingIntervalSeconds = 1,
            HistoryStorePath = "data/sync-history.db",
            HostWorkspacePath = "."
        });
        var nodeRegistry = new NodeRegistry();
        var planManager = new TestSyncPlanManager([]);
        var service = new SyncCoordinatorHostedService(
            configService,
            new TestHostEnvironment(_contentRootPath),
            nodeRegistry,
            planManager,
            NullLogger<SyncCoordinatorHostedService>.Instance);

        // Act
        await service.StartAsync(CancellationToken.None);
        try
        {
            await configService.WaitForGetSyncOptionsCallCountAsync(1);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        // Assert
        Assert.True(nodeRegistry.TryGet(SyncFrameworkOptions.DefaultHostNodeId, out var hostNode));
        Assert.NotNull(hostNode);
        Assert.True(hostNode.ConnectionSettings.TryGetValue("ExcludedAbsolutePaths", out var excludedPaths));

        var expectedHistoryPath = Path.GetFullPath(Path.Combine(_contentRootPath, "data/sync-history.db"));
        var excludedPathSet = excludedPaths
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains(expectedHistoryPath, excludedPathSet);
        Assert.Contains($"{expectedHistoryPath}-wal", excludedPathSet);
        Assert.Contains($"{expectedHistoryPath}-shm", excludedPathSet);
        Assert.Contains($"{expectedHistoryPath}-journal", excludedPathSet);
    }

    private sealed class TestConfigurationManagementService : IConfigurationManagementService
    {
        private readonly SyncFrameworkOptions _syncFrameworkOptions;
        private readonly TaskCompletionSource<bool> _firstCall = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _secondCall = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _getSyncOptionsCallCount;

        public TestConfigurationManagementService(SyncFrameworkOptions syncFrameworkOptions)
        {
            _syncFrameworkOptions = syncFrameworkOptions;
        }

        public string ConfigurationFilePath => Path.Combine(Path.GetTempPath(), "unused.yaml");

        public string GenerateDefaultYaml() => throw new NotSupportedException();

        public ServiceOptions GetServiceOptions() => throw new NotSupportedException();

        public LoggingOptions GetLoggingOptions() => throw new NotSupportedException();

        public InterfaceOptions GetInterfaceOptions() => throw new NotSupportedException();

        public PluginSystemOptions GetPluginSystemOptions() => throw new NotSupportedException();

        public SyncFrameworkOptions GetSyncOptions()
        {
            var currentCallCount = Interlocked.Increment(ref _getSyncOptionsCallCount);
            if (currentCallCount >= 1)
            {
                _firstCall.TrySetResult(true);
            }

            if (currentCallCount >= 2)
            {
                _secondCall.TrySetResult(true);
            }

            return _syncFrameworkOptions;
        }

        public AppConfigurationDocument GetCurrentConfiguration() => throw new NotSupportedException();

        public Task EnsureDefaultConfigurationFileAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AppConfigurationDocument> LoadAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task SaveAsync(AppConfigurationDocument configuration, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public void SetFrameworkEnabled(bool enabled)
        {
            _syncFrameworkOptions.EnableSyncFramework = enabled;
        }

        public async Task WaitForGetSyncOptionsCallCountAsync(int callCount)
        {
            if (callCount <= 1)
            {
                await _firstCall.Task.WaitAsync(TimeSpan.FromSeconds(2));
                return;
            }

            await _secondCall.Task.WaitAsync(TimeSpan.FromSeconds(3));
        }
    }

    private sealed class TestSyncPlanManager(IReadOnlyList<SyncPlan> plans) : ISyncPlanManager
    {
        private readonly TaskCompletionSource<bool> _executePlanNowSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ExecutePlanNowCallCount { get; private set; }

        public event Action<SyncPlan>? OnPlanCreated;

        public event Action<SyncPlan>? OnPlanUpdated;

        public event Action<SyncPlan>? OnPlanDeleted;

        public event Action<SyncPlan, bool>? OnPlanStatusChanged;

        public IReadOnlyList<SyncPlan> GetAllPlans() => plans;

        public SyncPlan? GetPlanById(string planId) => plans.FirstOrDefault(plan => string.Equals(plan.Id, planId, StringComparison.OrdinalIgnoreCase));

        public Task<SyncPlan> CreatePlanAsync(string name, string? description, string masterNodeId, string syncItemType, IEnumerable<SyncPlanSlaveConfiguration> slaveConfigurations, SyncSchedule schedule) => throw new NotSupportedException();

        public Task<SyncPlan> UpdatePlanAsync(string planId, Action<SyncPlan> updates) => throw new NotSupportedException();

        public Task<SyncPlan> ReplacePlanAsync(string planId, string name, string? description, string masterNodeId, string syncItemType, IEnumerable<SyncPlanSlaveConfiguration> slaveConfigurations, SyncSchedule schedule, bool isEnabled) => throw new NotSupportedException();

        public Task DeletePlanAsync(string planId) => throw new NotSupportedException();

        public Task EnablePlanAsync(string planId) => throw new NotSupportedException();

        public Task DisablePlanAsync(string planId) => throw new NotSupportedException();

        public Task<(bool IsValid, IReadOnlyList<string> Errors)> ValidatePlanAsync(string planId) => throw new NotSupportedException();

        public Task<Dictionary<string, SyncTaskResult>> ExecutePlanNowAsync(string planId, CancellationToken cancellationToken)
        {
            ExecutePlanNowCallCount++;
            _executePlanNowSignal.TrySetResult(true);
            return Task.FromResult(new Dictionary<string, SyncTaskResult>(StringComparer.OrdinalIgnoreCase));
        }

        public async Task WaitForExecutePlanNowAsync(TimeSpan timeout)
        {
            await _executePlanNowSignal.Task.WaitAsync(timeout);
        }
    }

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;

        public string ApplicationName { get; set; } = "UniversalSyncService.Host.IntegrationTests";

        public string ContentRootPath { get; set; } = contentRootPath;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(contentRootPath);
    }
}

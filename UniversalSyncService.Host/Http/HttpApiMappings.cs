using UniversalSyncService.Abstractions.Configuration;
using UniversalSyncService.Abstractions.Plugins;
using UniversalSyncService.Abstractions.SyncManagement.ConfigNodes;
using UniversalSyncService.Abstractions.SyncManagement;
using UniversalSyncService.Abstractions.SyncManagement.Engine;
using UniversalSyncService.Abstractions.SyncManagement.History;
using UniversalSyncService.Abstractions.SyncManagement.Tasks;
using UniversalSyncService.Abstractions.SyncManagement.Plans;
using UniversalSyncService.Abstractions.SyncItems;
using UniversalSyncService.Core.Nodes.OneDrive;
using UniversalSyncService.Core.Providers;
using UniversalSyncService.Core.SyncManagement.ConfigNodes;

namespace UniversalSyncService.Host.Http;

public static class HttpApiMappings
{
    public static WebApplication MapUniversalSyncHttpApi(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/health", () => TypedResults.Ok(new HealthResponse("Healthy")));

        app.MapGet("/api/public/interface-profile", (HttpContext httpContext, IConfigurationManagementService configurationManagementService) =>
        {
            var interfaceOptions = configurationManagementService.GetInterfaceOptions();
            var authRequired = interfaceOptions.RequireManagementApiKey
                && !(interfaceOptions.AllowAnonymousLoopback && httpContext.Connection.RemoteIpAddress is not null && System.Net.IPAddress.IsLoopback(httpContext.Connection.RemoteIpAddress));

            return TypedResults.Ok(new PublicInterfaceProfileResponse(
                interfaceOptions.EnableWebConsole,
                interfaceOptions.EnableHttpApi,
                interfaceOptions.EnableGrpc,
                authRequired));
        });

        var api = app.MapGroup("/api/v1");
        api.AddEndpointFilter<ApiKeyEndpointFilter>();

        api.MapGet("/status", (
            HostRuntimeState runtimeState,
            IConfigurationManagementService configurationManagementService,
            ISyncPlanManager syncPlanManager,
            ISyncEngine syncEngine,
            NodeRegistry nodeRegistry,
            IPluginManager pluginManager) =>
        {
            var serviceOptions = configurationManagementService.GetServiceOptions();
            return TypedResults.Ok(new StatusResponse(
                serviceOptions.ServiceName,
                runtimeState.StartedAt,
                UptimeSeconds: (long)(DateTimeOffset.UtcNow - runtimeState.StartedAt).TotalSeconds,
                PlanCount: syncPlanManager.GetAllPlans().Count,
                ActiveTaskCount: syncEngine.ActiveTasks.Count,
                NodeCount: nodeRegistry.GetAll().Count,
                LoadedPluginCount: pluginManager.LoadedPlugins.Count));
        });

        api.MapGet("/nodes", (NodeRegistry nodeRegistry, NodeProviderRegistry nodeProviderRegistry) =>
        {
            var nodes = nodeRegistry.GetAll()
                .OrderByDescending(node => string.Equals(node.Id, SyncFrameworkOptions.DefaultHostNodeId, StringComparison.OrdinalIgnoreCase))
                .ThenBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
                .Select(node => MapNodeSummary(node, nodeProviderRegistry))
                .ToList();
            return TypedResults.Ok(nodes);
        });

        api.MapGet("/nodes/{nodeId}", (string nodeId, NodeRegistry nodeRegistry, NodeProviderRegistry nodeProviderRegistry) =>
        {
            if (!nodeRegistry.TryGet(nodeId, out var node) || node is null)
            {
                return Results.NotFound();
            }

            return Results.Ok(MapNodeDetail(node, nodeProviderRegistry));
        });

        api.MapPost("/nodes", async (CreateOrUpdateNodeRequest request, IConfigurationManagementService configurationManagementService, NodeRegistry nodeRegistry, NodeProviderRegistry nodeProviderRegistry, CancellationToken cancellationToken) =>
        {
            var normalized = NormalizeNodeRequest(request, nodeProviderRegistry);
            if (normalized.Error is not null)
            {
                return Results.BadRequest(new ErrorResponse(normalized.Error));
            }

            if (string.Equals(normalized.Id, SyncFrameworkOptions.DefaultHostNodeId, StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new ErrorResponse("host-local 为隐式宿主节点，不支持通过节点管理接口创建。"));
            }

            if (nodeRegistry.TryGet(normalized.Id!, out _))
            {
                return Results.Conflict(new ErrorResponse($"节点已存在：{normalized.Id}"));
            }

            var node = CreateNodeConfiguration(normalized, normalized.CreatedAt);
            await nodeProviderRegistry.EnsureAuthenticatedAsync(node, cancellationToken);
            await PersistNodeAsync(configurationManagementService, node, replaceExisting: false, cancellationToken: default);
            nodeRegistry.Register(node);

            return Results.Created($"/api/v1/nodes/{node.Id}", MapNodeDetail(node, nodeProviderRegistry));
        });

        api.MapPut("/nodes/{nodeId}", async (string nodeId, CreateOrUpdateNodeRequest request, IConfigurationManagementService configurationManagementService, NodeRegistry nodeRegistry, NodeProviderRegistry nodeProviderRegistry, CancellationToken cancellationToken) =>
        {
            if (string.Equals(nodeId, SyncFrameworkOptions.DefaultHostNodeId, StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new ErrorResponse("host-local 为隐式宿主节点，只能在前端查看，不支持直接编辑。"));
            }

            if (!string.IsNullOrWhiteSpace(request.Id) && !string.Equals(request.Id, nodeId, StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new ErrorResponse("更新节点时，请求体中的节点 ID 必须与路由中的节点 ID 一致。"));
            }

            if (!nodeRegistry.TryGet(nodeId, out var currentNode) || currentNode is null)
            {
                return Results.NotFound();
            }

            var normalized = NormalizeNodeRequest(request, nodeProviderRegistry, nodeId, currentNode.CreatedAt);
            if (normalized.Error is not null)
            {
                return Results.BadRequest(new ErrorResponse(normalized.Error));
            }

            if (string.Equals(normalized.Id, SyncFrameworkOptions.DefaultHostNodeId, StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new ErrorResponse("host-local 为隐式宿主节点，只能在前端查看，不支持直接编辑。"));
            }

            var updatedNode = CreateNodeConfiguration(normalized, normalized.CreatedAt);
            await nodeProviderRegistry.EnsureAuthenticatedAsync(updatedNode, cancellationToken);
            await PersistNodeAsync(configurationManagementService, updatedNode, replaceExisting: true, cancellationToken: default);
            nodeRegistry.Register(updatedNode);

            return Results.Ok(MapNodeDetail(updatedNode, nodeProviderRegistry));
        });

        api.MapDelete("/nodes/{nodeId}", async (string nodeId, IConfigurationManagementService configurationManagementService, NodeRegistry nodeRegistry, ISyncPlanManager syncPlanManager) =>
        {
            if (string.Equals(nodeId, SyncFrameworkOptions.DefaultHostNodeId, StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new ErrorResponse("host-local 为隐式宿主节点，不支持删除。"));
            }

            if (!nodeRegistry.TryGet(nodeId, out var currentNode) || currentNode is null)
            {
                return Results.NotFound();
            }

            var referencingPlan = syncPlanManager.GetAllPlans()
                .FirstOrDefault(plan => string.Equals(plan.MasterNodeId, nodeId, StringComparison.OrdinalIgnoreCase)
                    || plan.SlaveConfigurations.Any(slave => string.Equals(slave.SlaveNodeId, nodeId, StringComparison.OrdinalIgnoreCase)));
            if (referencingPlan is not null)
            {
                return Results.BadRequest(new ErrorResponse($"节点仍被同步计划引用：{referencingPlan.Name}。"));
            }

            await DeleteNodeAsync(configurationManagementService, nodeId, cancellationToken: default);
            nodeRegistry.Remove(nodeId);

            return Results.NoContent();
        });

        api.MapGet("/plans", (ISyncPlanManager syncPlanManager) =>
        {
            var plans = syncPlanManager.GetAllPlans()
                .Select(MapPlanSummary)
                .ToList();
            return TypedResults.Ok(plans);
        });

        api.MapPost("/plans", async (CreateOrUpdatePlanRequest request, ISyncPlanManager syncPlanManager) =>
        {
            var createRequest = NormalizePlanRequest(request);
            if (createRequest.Error is not null)
            {
                return Results.BadRequest(new ErrorResponse(createRequest.Error));
            }

            var createdPlan = await syncPlanManager.CreatePlanAsync(
                createRequest.Name!,
                createRequest.Description,
                createRequest.MasterNodeId!,
                createRequest.SyncItemType!,
                createRequest.Slaves!,
                createRequest.Schedule!,
                createRequest.DeletionPolicy);

            if (!createRequest.IsEnabled)
            {
                await syncPlanManager.DisablePlanAsync(createdPlan.Id);
                createdPlan = syncPlanManager.GetPlanById(createdPlan.Id) ?? createdPlan;
            }

            return Results.Created($"/api/v1/plans/{createdPlan.Id}", MapPlanDetail(createdPlan));
        });

        api.MapGet("/plugins", (IPluginManager pluginManager) =>
        {
            var plugins = pluginManager.LoadedPlugins
                .Select(plugin => new PluginSummaryResponse(
                    plugin.Id,
                    plugin.Id,
                    plugin.Version,
                    plugin.Description))
                .ToList();

            return TypedResults.Ok(plugins);
        });

        api.MapGet("/history", async (int? limit, ISyncHistoryManager syncHistoryManager) =>
        {
            var entries = await syncHistoryManager.GetRecentHistoryAsync(planId: null, limit ?? 20);
            var response = entries.Select(MapHistoryEntry).ToList();
            return TypedResults.Ok(response);
        });

        api.MapGet("/config/summary", (IConfigurationManagementService configurationManagementService) =>
        {
            var serviceOptions = configurationManagementService.GetServiceOptions();
            var syncOptions = configurationManagementService.GetSyncOptions();
            var pluginOptions = configurationManagementService.GetPluginSystemOptions();

            return TypedResults.Ok(new ConfigSummaryResponse(
                serviceOptions.ServiceName,
                configurationManagementService.ConfigurationFilePath,
                syncOptions.EnableSyncFramework,
                syncOptions.HistoryStorePath,
                syncOptions.Nodes.Count,
                syncOptions.Plans.Count,
                pluginOptions.EnablePluginSystem,
                pluginOptions.PluginDirectory));
        });

        api.MapGet("/config/onedrive-defaults", () =>
        {
            var appCredentials = OneDriveAppCredentials.LoadCredentials();
            return TypedResults.Ok(new OneDriveDefaultsResponse(
                appCredentials is not null,
                appCredentials?.ClientId,
                appCredentials?.TenantId));
        });

        api.MapGet("/plans/{planId}", (string planId, ISyncPlanManager syncPlanManager) =>
        {
            var plan = syncPlanManager.GetPlanById(planId);
            if (plan is null)
            {
                return Results.NotFound();
            }

            return Results.Ok(MapPlanDetail(plan));
        });

        api.MapPut("/plans/{planId}", async (string planId, CreateOrUpdatePlanRequest request, ISyncPlanManager syncPlanManager) =>
        {
            if (syncPlanManager.GetPlanById(planId) is null)
            {
                return Results.NotFound();
            }

            var updateRequest = NormalizePlanRequest(request);
            if (updateRequest.Error is not null)
            {
                return Results.BadRequest(new ErrorResponse(updateRequest.Error));
            }

            var updatedPlan = await syncPlanManager.ReplacePlanAsync(
                planId,
                updateRequest.Name!,
                updateRequest.Description,
                updateRequest.MasterNodeId!,
                updateRequest.SyncItemType!,
                updateRequest.Slaves!,
                updateRequest.Schedule!,
                updateRequest.IsEnabled,
                updateRequest.DeletionPolicy);

            return Results.Ok(MapPlanDetail(updatedPlan));
        });

        api.MapDelete("/plans/{planId}", async (string planId, ISyncPlanManager syncPlanManager) =>
        {
            if (syncPlanManager.GetPlanById(planId) is null)
            {
                return Results.NotFound();
            }

            await syncPlanManager.DeletePlanAsync(planId);
            return Results.NoContent();
        });

        api.MapGet("/plans/{planId}/history", async (string planId, int? limit, ISyncHistoryManager syncHistoryManager) =>
        {
            var entries = await syncHistoryManager.GetRecentHistoryAsync(planId, limit ?? 20);
            var response = entries.Select(MapHistoryEntry).ToList();

            return TypedResults.Ok(response);
        });

        api.MapPost("/plans/{planId}/execute-now", async (string planId, ISyncPlanManager syncPlanManager, CancellationToken cancellationToken) =>
        {
            var plan = syncPlanManager.GetPlanById(planId);
            if (plan is null)
            {
                return Results.NotFound();
            }

            var results = await syncPlanManager.ExecutePlanNowAsync(planId, cancellationToken);
            return Results.Ok(new ExecutePlanResponse(
                planId,
                TotalTasks: results.Count,
                SuccessCount: results.Values.Count(result => result == SyncTaskResult.Success),
                NoChangesCount: results.Values.Count(result => result == SyncTaskResult.NoChanges),
                ConflictCount: results.Values.Count(result => result == SyncTaskResult.Conflict),
                FailedCount: results.Values.Count(result => result is SyncTaskResult.Failed or SyncTaskResult.Cancelled)));
        });

        return app;
    }

    private static PlanSummaryResponse MapPlanSummary(SyncPlan plan)
    {
        return new PlanSummaryResponse(
            plan.Id,
            plan.Name,
            plan.Description ?? string.Empty,
            plan.IsEnabled,
            plan.MasterNodeId,
            plan.SyncItemType,
            plan.ExecutionCount,
            plan.LastExecutedAt);
    }

    private static PlanDetailResponse MapPlanDetail(SyncPlan plan)
    {
        return new PlanDetailResponse(
            plan.Id,
            plan.Name,
            plan.Description ?? string.Empty,
            plan.IsEnabled,
            plan.MasterNodeId,
            plan.SyncItemType,
            plan.ExecutionCount,
            plan.LastExecutedAt,
            plan.Schedule.TriggerType.ToString(),
            plan.Schedule.CronExpression,
            plan.Schedule.Interval?.TotalSeconds,
            plan.Schedule.EnableFileSystemWatcher,
            MapDeletionPolicy(plan.DeletionPolicy),
            plan.SlaveConfigurations.Select(slave => new PlanSlaveResponse(
                slave.SlaveNodeId,
                slave.SyncMode.ToString(),
                slave.SourcePath,
                slave.TargetPath,
                slave.EnableDeletionProtection,
                slave.ConflictResolutionStrategy.ToString(),
                slave.Filters ?? [],
                slave.Exclusions ?? [])).ToList());
    }

    private static NodeSummaryResponse MapNodeSummary(NodeConfiguration node, NodeProviderRegistry nodeProviderRegistry)
    {
        var isImplicitHostNode = IsImplicitHostNode(node);
        return new NodeSummaryResponse(
            node.Id,
            node.Name,
            node.NodeType,
            node.IsEnabled,
            isImplicitHostNode,
            TryGetDisplayScopeBoundary(node, nodeProviderRegistry),
            isImplicitHostNode ? "宿主节点" : "已配置节点");
    }

    private static NodeDetailResponse MapNodeDetail(NodeConfiguration node, NodeProviderRegistry nodeProviderRegistry)
    {
        var isImplicitHostNode = IsImplicitHostNode(node);
        var displayScopeBoundary = TryGetDisplayScopeBoundary(node, nodeProviderRegistry);
        var displayConnectionSettings = new Dictionary<string, string>(node.ConnectionSettings, StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(displayScopeBoundary))
        {
            displayConnectionSettings["RootPath"] = displayScopeBoundary;
        }

        return new NodeDetailResponse(
            node.Id,
            node.Name,
            node.NodeType,
            node.IsEnabled,
            isImplicitHostNode,
            displayScopeBoundary,
            isImplicitHostNode ? "宿主节点" : "已配置节点",
            displayConnectionSettings,
            (node.CustomOptions ?? new Dictionary<string, object>())
                .ToDictionary(
                    pair => pair.Key,
                    pair => Convert.ToString(pair.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                    StringComparer.OrdinalIgnoreCase),
            node.CreatedAt,
            node.ModifiedAt);
    }

    private static bool IsImplicitHostNode(NodeConfiguration node)
    {
        return string.Equals(node.Id, SyncFrameworkOptions.DefaultHostNodeId, StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryGetDisplayScopeBoundary(NodeConfiguration node, NodeProviderRegistry nodeProviderRegistry)
    {
        node.ConnectionSettings.TryGetValue("RootPath", out var rootPath);
        if (rootPath is null)
        {
            return null;
        }

        try
        {
            return nodeProviderRegistry.GetDisplayScopeBoundary(node);
        }
        catch
        {
            // 【显示兜底】即使某个节点当前缺少可用 provider，也不要让节点列表/详情整体 500。
            // 至少返回配置中的原始 RootPath，保证前端还能看到并修复该节点配置。
            return rootPath;
        }
    }

    private static NormalizedNodeRequest NormalizeNodeRequest(
        CreateOrUpdateNodeRequest request,
        NodeProviderRegistry nodeProviderRegistry,
        string? fallbackId = null,
        DateTimeOffset? createdAt = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(nodeProviderRegistry);

        var id = string.IsNullOrWhiteSpace(request.Id) ? fallbackId : request.Id?.Trim();
        if (string.IsNullOrWhiteSpace(id))
        {
            return new NormalizedNodeRequest(Error: "节点 ID 不能为空。");
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return new NormalizedNodeRequest(Error: "节点名称不能为空。", Id: id);
        }

        if (string.IsNullOrWhiteSpace(request.NodeType))
        {
            return new NormalizedNodeRequest(Error: "节点类型不能为空。", Id: id);
        }

        var node = new NodeConfiguration(
            id,
            request.Name.Trim(),
            request.NodeType.Trim(),
            NormalizeDictionary(request.ConnectionSettings),
            createdAt);

        var normalizedNode = nodeProviderRegistry.NormalizeConfiguration(node);
        var validation = nodeProviderRegistry.ValidateConfiguration(normalizedNode);
        if (!validation.IsValid)
        {
            return new NormalizedNodeRequest(Error: validation.ErrorMessage, Id: id);
        }

        return new NormalizedNodeRequest(
            Id: id,
            Name: request.Name.Trim(),
            NodeType: request.NodeType.Trim(),
            IsEnabled: request.IsEnabled,
            ConnectionSettings: new Dictionary<string, string>(normalizedNode.ConnectionSettings, StringComparer.OrdinalIgnoreCase),
            CustomOptions: NormalizeDictionary(request.CustomOptions),
            CreatedAt: normalizedNode.CreatedAt,
            Error: null);
    }

    private static Dictionary<string, string> NormalizeDictionary(IReadOnlyDictionary<string, string>? values)
    {
        return values is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : values
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                .ToDictionary(
                    pair => pair.Key.Trim(),
                    pair => pair.Value.Trim(),
                    StringComparer.OrdinalIgnoreCase);
    }

    private static NodeConfiguration CreateNodeConfiguration(NormalizedNodeRequest request, DateTimeOffset? createdAt = null)
    {
        var node = new NodeConfiguration(
            request.Id!,
            request.Name!,
            request.NodeType!,
            new Dictionary<string, string>(request.ConnectionSettings!, StringComparer.OrdinalIgnoreCase),
            createdAt)
        {
            IsEnabled = request.IsEnabled,
            ModifiedAt = DateTimeOffset.Now,
            CustomOptions = request.CustomOptions!
                .ToDictionary(
                    pair => pair.Key,
                    pair => (object)pair.Value,
                    StringComparer.OrdinalIgnoreCase)
        };

        return node;
    }

    private static async Task PersistNodeAsync(IConfigurationManagementService configurationManagementService, NodeConfiguration node, bool replaceExisting, CancellationToken cancellationToken)
    {
        var configuration = await configurationManagementService.LoadAsync(cancellationToken);
        var configuredNodes = configuration.UniversalSyncService.Sync.Nodes;

        if (replaceExisting)
        {
            configuredNodes.RemoveAll(existing => string.Equals(existing.Id, node.Id, StringComparison.OrdinalIgnoreCase));
        }

        configuredNodes.Add(ToConfiguredNodeOptions(node));
        configuration.UniversalSyncService.Sync.Nodes = configuredNodes
            .OrderBy(existing => existing.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        await configurationManagementService.SaveAsync(configuration, cancellationToken);
    }

    private static async Task DeleteNodeAsync(IConfigurationManagementService configurationManagementService, string nodeId, CancellationToken cancellationToken)
    {
        var configuration = await configurationManagementService.LoadAsync(cancellationToken);
        configuration.UniversalSyncService.Sync.Nodes.RemoveAll(node => string.Equals(node.Id, nodeId, StringComparison.OrdinalIgnoreCase));
        await configurationManagementService.SaveAsync(configuration, cancellationToken);
    }

    private static ConfiguredNodeOptions ToConfiguredNodeOptions(NodeConfiguration node)
    {
        return new ConfiguredNodeOptions
        {
            Id = node.Id,
            Name = node.Name,
            NodeType = node.NodeType,
            ConnectionSettings = new Dictionary<string, string>(node.ConnectionSettings, StringComparer.OrdinalIgnoreCase),
            CustomOptions = (node.CustomOptions ?? new Dictionary<string, object>())
                .ToDictionary(
                    pair => pair.Key,
                    pair => Convert.ToString(pair.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                    StringComparer.OrdinalIgnoreCase),
            CreatedAt = node.CreatedAt,
            ModifiedAt = node.ModifiedAt,
            IsEnabled = node.IsEnabled
        };
    }

    private static NormalizedPlanRequest NormalizePlanRequest(CreateOrUpdatePlanRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return new NormalizedPlanRequest(Error: "计划名称不能为空。");
        }

        if (string.IsNullOrWhiteSpace(request.SyncItemType))
        {
            return new NormalizedPlanRequest(Error: "同步对象类型不能为空。");
        }

        if (request.Slaves is null || request.Slaves.Count == 0)
        {
            return new NormalizedPlanRequest(Error: "至少需要配置一个从节点。");
        }

        if (!Enum.TryParse<SyncTriggerType>(request.TriggerType, ignoreCase: true, out var triggerType))
        {
            return new NormalizedPlanRequest(Error: $"无法识别的触发方式：{request.TriggerType}");
        }

        var slaves = new List<SyncPlanSlaveConfiguration>();
        foreach (var slave in request.Slaves)
        {
            if (string.IsNullOrWhiteSpace(slave.SlaveNodeId))
            {
                return new NormalizedPlanRequest(Error: "从节点 ID 不能为空。");
            }

            if (!Enum.TryParse<SyncMode>(slave.SyncMode, ignoreCase: true, out var syncMode))
            {
                return new NormalizedPlanRequest(Error: $"无法识别的同步模式：{slave.SyncMode}");
            }

            slaves.Add(new SyncPlanSlaveConfiguration(slave.SlaveNodeId, syncMode)
            {
                SourcePath = NormalizeOptionalText(slave.SourcePath),
                TargetPath = NormalizeOptionalText(slave.TargetPath),
                EnableDeletionProtection = slave.EnableDeletionProtection,
                ConflictResolutionStrategy = NormalizeConflictResolutionStrategy(slave.ConflictResolutionStrategy),
                Filters = NormalizeTextList(slave.Filters),
                Exclusions = NormalizeTextList(slave.Exclusions)
            });
        }

        var schedule = new SyncSchedule(triggerType)
        {
            CronExpression = NormalizeOptionalText(request.CronExpression),
            Interval = request.IntervalSeconds is > 0 ? TimeSpan.FromSeconds(request.IntervalSeconds.Value) : null,
            EnableFileSystemWatcher = request.EnableFileSystemWatcher
        };

        var normalizedDeletionPolicy = NormalizeDeletionPolicyRequest(request.DeletionPolicy);
        if (normalizedDeletionPolicy.Error is not null)
        {
            return new NormalizedPlanRequest(Error: normalizedDeletionPolicy.Error);
        }

        return new NormalizedPlanRequest(
            request.Name.Trim(),
            NormalizeOptionalText(request.Description),
            NormalizeMasterNodeId(request.MasterNodeId),
            SyncItemKinds.Normalize(request.SyncItemType),
            request.IsEnabled,
            schedule,
            slaves,
            normalizedDeletionPolicy.Policy,
            Error: null);
    }

    private static PlanDeletionPolicyResponse MapDeletionPolicy(SyncPlanDeletionPolicy policy)
    {
        return new PlanDeletionPolicyResponse(
            policy.DeleteThreshold,
            policy.PercentThreshold,
            policy.FailSafeMode.ToString(),
            policy.AllowThresholdBreachForCurrentRun,
            policy.ThresholdOverrideReason);
    }

    private static NormalizedDeletionPolicy NormalizeDeletionPolicyRequest(PlanDeletionPolicyRequest? request)
    {
        if (request is null)
        {
            return new NormalizedDeletionPolicy(null, null);
        }

        if (!Enum.TryParse<SyncPlanFailSafeMode>(request.FailSafeMode, ignoreCase: true, out var failSafeMode))
        {
            return new NormalizedDeletionPolicy(null, $"无法识别的删除 fail-safe 模式：{request.FailSafeMode}");
        }

        if (request.DeleteThreshold <= 0)
        {
            return new NormalizedDeletionPolicy(null, "删除阈值必须大于 0。");
        }

        if (request.PercentThreshold <= 0 || request.PercentThreshold > 100)
        {
            return new NormalizedDeletionPolicy(null, "删除百分比阈值必须在 (0, 100] 区间。");
        }

        if (request.AllowThresholdBreachForCurrentRun && string.IsNullOrWhiteSpace(request.ThresholdOverrideReason))
        {
            return new NormalizedDeletionPolicy(null, "启用本轮阈值越权时必须提供审核原因。");
        }

        var policy = new SyncPlanDeletionPolicy
        {
            DeleteThreshold = request.DeleteThreshold,
            PercentThreshold = request.PercentThreshold,
            FailSafeMode = failSafeMode,
            AllowThresholdBreachForCurrentRun = request.AllowThresholdBreachForCurrentRun,
            ThresholdOverrideReason = NormalizeOptionalText(request.ThresholdOverrideReason)
        };

        return new NormalizedDeletionPolicy(policy, null);
    }

    private static string NormalizeMasterNodeId(string? masterNodeId)
    {
        return string.IsNullOrWhiteSpace(masterNodeId)
            ? SyncFrameworkOptions.DefaultHostNodeId
            : masterNodeId.Trim();
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static List<string> NormalizeTextList(IReadOnlyList<string>? values)
    {
        return values is null
            ? []
            : values
                .Select(value => value?.Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Cast<string>()
                .ToList();
    }

    private static ConflictResolutionStrategy NormalizeConflictResolutionStrategy(string? strategy)
    {
        if (string.IsNullOrWhiteSpace(strategy))
        {
            return ConflictResolutionStrategy.Manual;
        }

        return Enum.TryParse<ConflictResolutionStrategy>(strategy, ignoreCase: true, out var parsed)
            ? parsed
            : ConflictResolutionStrategy.Manual;
    }

    private static HistoryEntryResponse MapHistoryEntry(SyncHistoryEntry entry)
    {
        return new HistoryEntryResponse(
            entry.Id,
            entry.PlanId,
            entry.TaskId,
            entry.NodeId,
            entry.Metadata.Path,
            entry.Metadata.Name,
            entry.Metadata.Size,
            entry.State.ToString(),
            entry.SyncTimestamp,
            entry.SyncVersion,
            entry.Metadata.Checksum);
    }

    public sealed record HealthResponse(string Status);

    public sealed record PublicInterfaceProfileResponse(
        bool EnableWebConsole,
        bool EnableHttpApi,
        bool EnableGrpc,
        bool RequireManagementApiKey);

    public sealed record StatusResponse(
        string ServiceName,
        DateTimeOffset StartedAt,
        long UptimeSeconds,
        int PlanCount,
        int ActiveTaskCount,
        int NodeCount,
        int LoadedPluginCount);

    public sealed record NodeSummaryResponse(string Id, string Name, string NodeType, bool IsEnabled, bool IsImplicitHostNode, string? RootPath, string SourceLabel);

    public sealed record NodeDetailResponse(
        string Id,
        string Name,
        string NodeType,
        bool IsEnabled,
        bool IsImplicitHostNode,
        string? RootPath,
        string SourceLabel,
        IReadOnlyDictionary<string, string> ConnectionSettings,
        IReadOnlyDictionary<string, string> CustomOptions,
        DateTimeOffset CreatedAt,
        DateTimeOffset ModifiedAt);

    public sealed record CreateOrUpdateNodeRequest(
        string? Id,
        string Name,
        string NodeType,
        bool IsEnabled,
        Dictionary<string, string>? ConnectionSettings,
        Dictionary<string, string>? CustomOptions);

    public sealed record PlanSummaryResponse(
        string Id,
        string Name,
        string Description,
        bool IsEnabled,
        string MasterNodeId,
        string SyncItemType,
        int ExecutionCount,
        DateTimeOffset? LastExecutedAt);

    public sealed record PlanDetailResponse(
        string Id,
        string Name,
        string Description,
        bool IsEnabled,
        string MasterNodeId,
        string SyncItemType,
        int ExecutionCount,
        DateTimeOffset? LastExecutedAt,
        string TriggerType,
        string? CronExpression,
        double? IntervalSeconds,
        bool EnableFileSystemWatcher,
        PlanDeletionPolicyResponse DeletionPolicy,
        IReadOnlyList<PlanSlaveResponse> Slaves);

    public sealed record PlanDeletionPolicyResponse(
        int DeleteThreshold,
        double PercentThreshold,
        string FailSafeMode,
        bool AllowThresholdBreachForCurrentRun,
        string? ThresholdOverrideReason);

    public sealed record PlanSlaveResponse(
        string SlaveNodeId,
        string SyncMode,
        string? SourcePath,
        string? TargetPath,
        bool EnableDeletionProtection,
        string ConflictResolutionStrategy,
        IReadOnlyList<string> Filters,
        IReadOnlyList<string> Exclusions);

    public sealed record CreateOrUpdatePlanRequest(
        string Name,
        string? Description,
        string? MasterNodeId,
        string SyncItemType,
        bool IsEnabled,
        string TriggerType,
        string? CronExpression,
        double? IntervalSeconds,
        bool EnableFileSystemWatcher,
        List<PlanSlaveRequest> Slaves,
        PlanDeletionPolicyRequest? DeletionPolicy = null);

    public sealed record PlanDeletionPolicyRequest(
        int DeleteThreshold = SyncPlanDeletionPolicy.DefaultDeleteThreshold,
        double PercentThreshold = SyncPlanDeletionPolicy.DefaultPercentThreshold,
        string FailSafeMode = "Block",
        bool AllowThresholdBreachForCurrentRun = false,
        string? ThresholdOverrideReason = null);

    public sealed record PlanSlaveRequest(
        string SlaveNodeId,
        string SyncMode,
        string? SourcePath,
        string? TargetPath,
        bool EnableDeletionProtection,
        string? ConflictResolutionStrategy,
        List<string>? Filters,
        List<string>? Exclusions);

    private sealed record NormalizedPlanRequest(
        string? Name = null,
        string? Description = null,
        string? MasterNodeId = null,
        string? SyncItemType = null,
        bool IsEnabled = true,
        SyncSchedule? Schedule = null,
        List<SyncPlanSlaveConfiguration>? Slaves = null,
        SyncPlanDeletionPolicy? DeletionPolicy = null,
        string? Error = null);

    private sealed record NormalizedDeletionPolicy(
        SyncPlanDeletionPolicy? Policy,
        string? Error);

    private sealed record NormalizedNodeRequest(
        string? Id = null,
        string? Name = null,
        string? NodeType = null,
        bool IsEnabled = true,
        Dictionary<string, string>? ConnectionSettings = null,
        Dictionary<string, string>? CustomOptions = null,
        DateTimeOffset? CreatedAt = null,
        string? Error = null);

    public sealed record ErrorResponse(string Error);

    public sealed record PluginSummaryResponse(
        string Id,
        string Name,
        string Version,
        string Description);

    public sealed record ConfigSummaryResponse(
        string ServiceName,
        string ConfigurationFilePath,
        bool EnableSyncFramework,
        string HistoryStorePath,
        int NodeCount,
        int PlanCount,
        bool EnablePluginSystem,
        string PluginDirectory);

    public sealed record OneDriveDefaultsResponse(
        bool IsConfigured,
        string? ClientId,
        string? TenantId);

    public sealed record ExecutePlanResponse(
        string PlanId,
        int TotalTasks,
        int SuccessCount,
        int NoChangesCount,
        int ConflictCount,
        int FailedCount);

    public sealed record HistoryEntryResponse(
        string Id,
        string PlanId,
        string TaskId,
        string NodeId,
        string Path,
        string Name,
        long Size,
        string State,
        DateTimeOffset SyncTimestamp,
        long SyncVersion,
        string? Checksum);
}

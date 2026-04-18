using Grpc.Core;
using UniversalSyncService.Abstractions.SyncManagement;
using UniversalSyncService.Abstractions.SyncManagement.History;
using UniversalSyncService.Abstractions.SyncManagement.Tasks;

namespace UniversalSyncService.Host.Grpc;

/// <summary>
/// 对外 gRPC 接口层。
/// 这里只做协议转换与错误映射，真正的业务仍由 Core 服务负责。
/// </summary>
public sealed class SyncApiService : SyncApi.SyncApiBase
{
    private const int MaxHistoryLimit = 200;

    private readonly ISyncPlanManager _syncPlanManager;
    private readonly ISyncHistoryManager _syncHistoryManager;

    public SyncApiService(
        ISyncPlanManager syncPlanManager,
        ISyncHistoryManager syncHistoryManager)
    {
        _syncPlanManager = syncPlanManager;
        _syncHistoryManager = syncHistoryManager;
    }

    public override Task<ListPlansResponse> ListPlans(ListPlansRequest request, ServerCallContext context)
    {
        var response = new ListPlansResponse();
        foreach (var plan in _syncPlanManager.GetAllPlans())
        {
            response.Plans.Add(new SyncPlanItem
            {
                Id = plan.Id,
                Name = plan.Name,
                Description = plan.Description ?? string.Empty,
                IsEnabled = plan.IsEnabled,
                MasterNodeId = plan.MasterNodeId,
                SyncItemType = plan.SyncItemType,
                ExecutionCount = plan.ExecutionCount,
                LastExecutedAt = plan.LastExecutedAt?.ToString("O") ?? string.Empty
            });
        }

        return Task.FromResult(response);
    }

    public override async Task<ExecutePlanNowResponse> ExecutePlanNow(ExecutePlanNowRequest request, ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.PlanId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "plan_id 不能为空。"));
        }

        var existingPlan = _syncPlanManager.GetPlanById(request.PlanId);
        if (existingPlan is null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"未找到同步计划：{request.PlanId}"));
        }

        var results = await _syncPlanManager.ExecutePlanNowAsync(request.PlanId, context.CancellationToken);
        return new ExecutePlanNowResponse
        {
            PlanId = request.PlanId,
            TotalTasks = results.Count,
            SuccessCount = results.Values.Count(result => result == SyncTaskResult.Success),
            NoChangesCount = results.Values.Count(result => result == SyncTaskResult.NoChanges),
            ConflictCount = results.Values.Count(result => result == SyncTaskResult.Conflict),
            FailedCount = results.Values.Count(result => result is SyncTaskResult.Failed or SyncTaskResult.Cancelled)
        };
    }

    public override async Task<GetRecentHistoryResponse> GetRecentHistory(GetRecentHistoryRequest request, ServerCallContext context)
    {
        if (request.Limit > MaxHistoryLimit)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"limit 不能超过 {MaxHistoryLimit}。"));
        }

        var entries = await _syncHistoryManager.GetRecentHistoryAsync(
            string.IsNullOrWhiteSpace(request.PlanId) ? null : request.PlanId,
            request.Limit <= 0 ? 20 : request.Limit);

        var response = new GetRecentHistoryResponse();
        foreach (var entry in entries)
        {
            response.Entries.Add(new SyncHistoryItem
            {
                Id = entry.Id,
                PlanId = entry.PlanId,
                TaskId = entry.TaskId,
                NodeId = entry.NodeId,
                Path = entry.Metadata.Path,
                Name = entry.Metadata.Name,
                Size = entry.Metadata.Size,
                State = entry.State.ToString(),
                SyncTimestamp = entry.SyncTimestamp.ToString("O"),
                SyncVersion = entry.SyncVersion,
                Checksum = entry.Metadata.Checksum ?? string.Empty
            });
        }

        return response;
    }
}

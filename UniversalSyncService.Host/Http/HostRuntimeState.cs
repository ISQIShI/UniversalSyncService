namespace UniversalSyncService.Host.Http;

/// <summary>
/// 记录当前宿主进程的运行期状态。
/// 这里优先保存“何时启动”这类轻量信息，供状态接口和前端展示使用。
/// </summary>
public sealed class HostRuntimeState
{
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;
}

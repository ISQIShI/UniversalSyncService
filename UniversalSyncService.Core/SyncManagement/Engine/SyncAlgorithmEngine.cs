using Microsoft.Extensions.Logging;
using UniversalSyncService.Abstractions.SyncItems;
using UniversalSyncService.Abstractions.SyncManagement.Engine;
using UniversalSyncService.Abstractions.SyncManagement.History;
using UniversalSyncService.Abstractions.SyncManagement.Plans;

namespace UniversalSyncService.Core.SyncManagement.Engine;

/// <summary>
/// 同步决策引擎。
/// 这里把“当前快照 + 主/从各自历史锚点”的解释集中到算法层，
/// 避免执行层再额外补丁式覆写删除或修改决策。
/// </summary>
public sealed class SyncAlgorithmEngine : ISyncAlgorithmEngine
{
    private readonly ILogger<SyncAlgorithmEngine> _logger;

    public SyncAlgorithmEngine(ILogger<SyncAlgorithmEngine> logger)
    {
        _logger = logger;
    }

    public SyncDecision CalculateDecision(SyncPathSyncContext context, SyncMode syncMode)
    {
        ArgumentNullException.ThrowIfNull(context);

        // 首次同步完全没有历史锚点时，直接按当前两边状态建立基线决策。
        if (context.MasterHistoryEntry is null && context.SlaveHistoryEntry is null)
        {
            return CalculateInitialDecision(context.MasterMetadata, context.SlaveMetadata, syncMode);
        }

        var masterState = DetermineState(context.MasterMetadata, context.MasterHistoryEntry);
        var slaveState = DetermineState(context.SlaveMetadata, context.SlaveHistoryEntry);

        var decision = syncMode switch
        {
            SyncMode.Bidirectional => CalculateBidirectionalDecision(masterState, slaveState),
            SyncMode.Push => CalculatePushDecision(masterState, slaveState),
            SyncMode.Pull => CalculatePullDecision(masterState, slaveState),
            SyncMode.PushAndDelete => CalculatePushAndDeleteDecision(masterState, slaveState),
            SyncMode.PullAndDelete => CalculatePullAndDeleteDecision(masterState, slaveState),
            _ => SyncDecision.Conflict
        };

        _logger.LogDebug(
            "已计算同步决策。路径={Path}，模式={SyncMode}，主状态={MasterState}，从状态={SlaveState}，决策={Decision}",
            context.Path,
            syncMode,
            masterState,
            slaveState,
            decision);

        return decision;
    }

    public Task<Dictionary<string, SyncDecision>> CalculateDecisionsAsync(
        IEnumerable<SyncPathSyncContext> contexts,
        SyncMode syncMode)
    {
        ArgumentNullException.ThrowIfNull(contexts);

        var decisions = new Dictionary<string, SyncDecision>(StringComparer.OrdinalIgnoreCase);
        foreach (var context in contexts)
        {
            // 运行时 context 重构：
            // 双方当前都不存在且不是显式删除候选时，不进入决策链，
            // 防止历史残留路径在算法层被再次当作有效输入。
            if (context.MasterMetadata is null
                && context.SlaveMetadata is null
                && !context.IsExplicitDeleteCandidate)
            {
                continue;
            }

            decisions[context.Path] = CalculateDecision(context, syncMode);
        }

        return Task.FromResult(decisions);
    }

    private static SyncDecision CalculateInitialDecision(SyncItemMetadata? masterMetadata, SyncItemMetadata? slaveMetadata, SyncMode syncMode)
    {
        if (masterMetadata is null && slaveMetadata is null)
        {
            return SyncDecision.DoNothing;
        }

        if (masterMetadata is not null && slaveMetadata is null)
        {
            return syncMode is SyncMode.Bidirectional or SyncMode.Push or SyncMode.PushAndDelete
                ? SyncDecision.Push
                : SyncDecision.DoNothing;
        }

        if (masterMetadata is null && slaveMetadata is not null)
        {
            return syncMode switch
            {
                SyncMode.Bidirectional or SyncMode.Pull or SyncMode.PullAndDelete => SyncDecision.Pull,
                SyncMode.PushAndDelete => SyncDecision.DeleteRemote,
                _ => SyncDecision.DoNothing
            };
        }

        if (masterMetadata is null || slaveMetadata is null)
        {
            return SyncDecision.DoNothing;
        }

        if (!HasChangedAcrossNodes(masterMetadata, slaveMetadata))
        {
            return SyncDecision.DoNothing;
        }

        return syncMode switch
        {
            SyncMode.Push or SyncMode.PushAndDelete => SyncDecision.Push,
            SyncMode.Pull or SyncMode.PullAndDelete => SyncDecision.Pull,
            _ => ResolveByModifiedAt(masterMetadata, slaveMetadata)
        };
    }

    private static ItemState DetermineState(SyncItemMetadata? currentMetadata, SyncHistoryEntry? historyEntry)
    {
        if (currentMetadata is null)
        {
            return historyEntry is null || historyEntry.State == FileHistoryState.Deleted
                ? ItemState.Missing
                : ItemState.Deleted;
        }

        if (historyEntry is null || historyEntry.State == FileHistoryState.Deleted)
        {
            return ItemState.Created;
        }

        return HasChangedComparedToHistory(currentMetadata, historyEntry.Metadata)
            ? ItemState.Modified
            : ItemState.Unchanged;
    }

    private static bool HasChangedAcrossNodes(SyncItemMetadata current, SyncItemMetadata previous)
    {
        if (!string.Equals(current.ContentType, previous.ContentType, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (current.Size != previous.Size)
        {
            return true;
        }

        // 【跨节点比较】不同节点的摘要算法可能不同（例如本地 SHA256 vs OneDrive QuickXorHash），
        // 因此这里仍以时间戳为主，不直接跨节点比较 checksum，避免把“算法不同”误判成内容已改。
        if (current.ModifiedAt.HasValue && previous.ModifiedAt.HasValue)
        {
            return current.ModifiedAt != previous.ModifiedAt;
        }

        return false;
    }

    private static bool HasChangedComparedToHistory(SyncItemMetadata current, SyncItemMetadata previous)
    {
        if (!string.Equals(current.ContentType, previous.ContentType, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (current.Size != previous.Size)
        {
            return true;
        }

        // 【历史对比】同一节点前后两次快照的摘要算法稳定，
        // 因此当时间戳精度不足或被保留时，应优先用 checksum 识别“同大小但内容已变”的更新。
        if (!string.IsNullOrWhiteSpace(current.Checksum) && !string.IsNullOrWhiteSpace(previous.Checksum))
        {
            return !string.Equals(current.Checksum, previous.Checksum, StringComparison.OrdinalIgnoreCase);
        }

        if (current.ModifiedAt.HasValue && previous.ModifiedAt.HasValue)
        {
            return current.ModifiedAt != previous.ModifiedAt;
        }

        return false;
    }

    private static SyncDecision ResolveByModifiedAt(SyncItemMetadata masterMetadata, SyncItemMetadata slaveMetadata)
    {
        if (masterMetadata.ModifiedAt.HasValue && slaveMetadata.ModifiedAt.HasValue)
        {
            return masterMetadata.ModifiedAt >= slaveMetadata.ModifiedAt
                ? SyncDecision.Push
                : SyncDecision.Pull;
        }

        return masterMetadata.Size >= slaveMetadata.Size
            ? SyncDecision.Push
            : SyncDecision.Pull;
    }

    private static SyncDecision CalculateBidirectionalDecision(ItemState masterState, ItemState slaveState)
    {
        return (masterState, slaveState) switch
        {
            (ItemState.Missing, ItemState.Missing) => SyncDecision.DoNothing,
            (ItemState.Deleted, ItemState.Deleted) => SyncDecision.CleanHistory,
            (ItemState.Unchanged, ItemState.Unchanged) => SyncDecision.DoNothing,

            (ItemState.Unchanged, ItemState.Modified) => SyncDecision.Pull,
            (ItemState.Unchanged, ItemState.Created) => SyncDecision.Pull,
            (ItemState.Unchanged, ItemState.Deleted) => SyncDecision.DeleteLocal,

            (ItemState.Modified, ItemState.Unchanged) => SyncDecision.Push,
            (ItemState.Created, ItemState.Unchanged) => SyncDecision.Push,
            (ItemState.Deleted, ItemState.Unchanged) => SyncDecision.DeleteRemote,

            (ItemState.Modified, ItemState.Deleted) => SyncDecision.Push,
            (ItemState.Created, ItemState.Deleted) => SyncDecision.Push,
            (ItemState.Deleted, ItemState.Modified) => SyncDecision.Pull,
            (ItemState.Deleted, ItemState.Created) => SyncDecision.Pull,

            (ItemState.Missing, ItemState.Created) => SyncDecision.Pull,
            (ItemState.Created, ItemState.Missing) => SyncDecision.Push,
            (ItemState.Missing, ItemState.Deleted) => SyncDecision.CleanHistory,
            (ItemState.Deleted, ItemState.Missing) => SyncDecision.CleanHistory,

            _ => SyncDecision.Conflict
        };
    }

    private static SyncDecision CalculatePushDecision(ItemState masterState, ItemState slaveState)
    {
        return (masterState, slaveState) switch
        {
            (ItemState.Missing, ItemState.Missing) => SyncDecision.DoNothing,
            (ItemState.Deleted, ItemState.Deleted) => SyncDecision.CleanHistory,
            (ItemState.Unchanged, ItemState.Unchanged) => SyncDecision.DoNothing,

            (ItemState.Modified, ItemState.Unchanged) => SyncDecision.Push,
            (ItemState.Created, ItemState.Unchanged) => SyncDecision.Push,
            (ItemState.Created, ItemState.Missing) => SyncDecision.Push,
            (ItemState.Modified, ItemState.Deleted) => SyncDecision.Push,
            (ItemState.Created, ItemState.Deleted) => SyncDecision.Push,

            (ItemState.Deleted, ItemState.Unchanged) => SyncDecision.DeleteRemote,
            (ItemState.Deleted, ItemState.Missing) => SyncDecision.CleanHistory,
            (ItemState.Unchanged, ItemState.Deleted) => SyncDecision.DoNothing,

            _ => SyncDecision.Conflict
        };
    }

    private static SyncDecision CalculatePullDecision(ItemState masterState, ItemState slaveState)
    {
        return (masterState, slaveState) switch
        {
            (ItemState.Missing, ItemState.Missing) => SyncDecision.DoNothing,
            (ItemState.Deleted, ItemState.Deleted) => SyncDecision.CleanHistory,
            (ItemState.Unchanged, ItemState.Unchanged) => SyncDecision.DoNothing,

            (ItemState.Unchanged, ItemState.Modified) => SyncDecision.Pull,
            (ItemState.Unchanged, ItemState.Created) => SyncDecision.Pull,
            (ItemState.Deleted, ItemState.Modified) => SyncDecision.Pull,
            (ItemState.Deleted, ItemState.Created) => SyncDecision.Pull,

            (ItemState.Unchanged, ItemState.Deleted) => SyncDecision.DeleteLocal,
            (ItemState.Missing, ItemState.Deleted) => SyncDecision.CleanHistory,
            (ItemState.Deleted, ItemState.Unchanged) => SyncDecision.DoNothing,

            _ => SyncDecision.Conflict
        };
    }

    private static SyncDecision CalculatePushAndDeleteDecision(ItemState masterState, ItemState slaveState)
    {
        return (masterState, slaveState) switch
        {
            (ItemState.Deleted, ItemState.Unchanged) => SyncDecision.DeleteRemote,
            (ItemState.Deleted, ItemState.Missing) => SyncDecision.CleanHistory,
            _ => CalculatePushDecision(masterState, slaveState)
        };
    }

    private static SyncDecision CalculatePullAndDeleteDecision(ItemState masterState, ItemState slaveState)
    {
        return (masterState, slaveState) switch
        {
            (ItemState.Unchanged, ItemState.Deleted) => SyncDecision.DeleteLocal,
            (ItemState.Missing, ItemState.Deleted) => SyncDecision.CleanHistory,
            _ => CalculatePullDecision(masterState, slaveState)
        };
    }

    private enum ItemState
    {
        Missing,
        Unchanged,
        Modified,
        Deleted,
        Created,
    }
}

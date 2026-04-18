using System.Collections.Concurrent;
using UniversalSyncService.Abstractions.SyncManagement.ConfigNodes;

namespace UniversalSyncService.Core.SyncManagement.ConfigNodes;

/// <summary>
/// 节点注册表，用于管理同步节点配置。
/// </summary>
public sealed class NodeRegistry
{
    private readonly ConcurrentDictionary<string, NodeConfiguration> _nodes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 注册或更新节点配置。
    /// </summary>
    public void Register(NodeConfiguration node)
    {
        ArgumentNullException.ThrowIfNull(node);
        _nodes[node.Id] = node;
    }

    /// <summary>
    /// 尝试获取节点配置。
    /// </summary>
    public bool TryGet(string nodeId, out NodeConfiguration? node)
    {
        ArgumentNullException.ThrowIfNull(nodeId);
        return _nodes.TryGetValue(nodeId, out node);
    }

    /// <summary>
    /// 获取指定节点配置。
    /// </summary>
    public NodeConfiguration? GetById(string nodeId)
    {
        ArgumentNullException.ThrowIfNull(nodeId);
        _nodes.TryGetValue(nodeId, out var node);
        return node;
    }

    /// <summary>
    /// 获取所有节点配置。
    /// </summary>
    public IReadOnlyCollection<NodeConfiguration> GetAll()
        => _nodes.Values.ToArray();

    /// <summary>
    /// 移除节点配置。
    /// </summary>
    public bool Remove(string nodeId)
    {
        ArgumentNullException.ThrowIfNull(nodeId);
        return _nodes.TryRemove(nodeId, out _);
    }

    /// <summary>
    /// 清空所有节点配置。
    /// </summary>
    public void Clear() => _nodes.Clear();
}

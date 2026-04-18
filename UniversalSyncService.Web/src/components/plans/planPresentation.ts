import type { NodeSummary } from '../../api.ts';

export const HOST_LOCAL_NODE_ID = 'host-local';

export const conflictResolutionOptions = [
  { value: 'Manual', label: '手动处理' },
  { value: 'KeepNewer', label: '保留最新' },
  { value: 'KeepLocal', label: '保留主节点版本' },
  { value: 'KeepRemote', label: '保留从节点版本' },
] as const;

export const conflictResolutionDescriptions: Record<string, string> = {
  Manual: '系统遇到冲突时直接终止该文件的同步并报警，交由人工介入处理。',
  KeepNewer: '比较两端文件的修改时间，自动保留时间戳较新的版本。',
  KeepLocal: '当两端同时发生变更导致冲突时，无条件以主节点的文件内容为准。',
  KeepRemote: '当两端同时发生变更导致冲突时，无条件以从节点的文件内容为准。',
};

export const syncModeOptions = [
  { value: 'Bidirectional', label: '双向同步' },
  { value: 'Push', label: '主节点推送' },
  { value: 'Pull', label: '主节点拉取' },
  { value: 'PushAndDelete', label: '推送并删除' },
  { value: 'PullAndDelete', label: '拉取并删除' },
] as const;

export const triggerTypeOptions = [
  { value: 'Manual', label: '手动触发' },
  { value: 'Scheduled', label: '定时触发' },
  { value: 'Realtime', label: '实时触发' },
] as const;

export function formatNodeLabel(node: NodeSummary | undefined, fallbackId: string) {
  if (!node) {
    return fallbackId;
  }

  if (node.isImplicitHostNode) {
    return `🖥️ ${node.name} (host-local)`;
  }

  return `📦 ${node.name} (${node.id})`;
}

export function formatMasterNodeLabel(masterNodeId: string, nodes: NodeSummary[]) {
  const effectiveNodeId = getEffectiveMasterNodeId(masterNodeId);
  const node = nodes.find((item) => item.id === effectiveNodeId);
  return formatNodeLabel(node, effectiveNodeId);
}

export function formatTriggerTypeLabel(triggerType: string) {
  return triggerTypeOptions.find((option) => option.value === triggerType)?.label ?? triggerType;
}

export function formatSyncModeLabel(syncMode: string) {
  return syncModeOptions.find((option) => option.value === syncMode)?.label ?? syncMode;
}

export function formatConflictResolutionStrategyLabel(strategy: string | undefined) {
  switch (strategy) {
    case 'KeepNewer':
      return '保留最新';
    case 'Manual':
    case undefined:
    case '':
      return '手动处理';
    case 'KeepLocal':
      return '保留主节点版本';
    case 'KeepRemote':
      return '保留从节点版本';
    case 'KeepLarger':
      return '保留较大文件';
    case 'RenameBoth':
      return '双方改名保留';
    default:
      return `未支持策略（${strategy}）`;
  }
}

export function isAbsolutePlanPath(path: string | undefined) {
  if (!path) {
    return false;
  }

  const trimmed = path.trim();
  return trimmed.startsWith('/')
    || trimmed.startsWith('\\')
    || trimmed.startsWith('\\\\')
    || trimmed.startsWith('\\?\\')
    || trimmed.startsWith('\\.\\')
    || /^[A-Za-z]:([\\/]|$)/.test(trimmed);
}

export function canUseAbsolutePath(node: NodeSummary | undefined) {
  return Boolean(node?.isImplicitHostNode || node?.nodeType?.toLowerCase() === 'local');
}

export function getPathRuleHint(node: NodeSummary | undefined) {
  if (node?.isImplicitHostNode) {
    return '宿主默认主节点支持显式绝对路径，也可以继续使用相对于工作区的路径。';
  }

  if (node?.nodeType?.toLowerCase() === 'local') {
    return '本地节点支持显式绝对路径，也支持相对于节点根路径的相对路径。';
  }

  return '普通节点只支持相对于节点根路径的相对路径。';
}

export function getEffectiveMasterNodeId(masterNodeId: string | undefined) {
  return masterNodeId?.trim() ? masterNodeId : HOST_LOCAL_NODE_ID;
}

export function getNodeById(nodes: NodeSummary[], nodeId: string | undefined) {
  const effectiveNodeId = getEffectiveMasterNodeId(nodeId);
  return nodes.find((node) => node.id === effectiveNodeId);
}

export function formatAbsolutePathValidationError(pathLabel: string, node: NodeSummary | undefined) {
  const nodeLabel = node?.isImplicitHostNode ? '🖥️ 宿主默认主节点 (host-local)' : (node?.name ? `📦 ${node.name}` : '📦 当前节点');
  return `${pathLabel}使用了绝对路径，但 ${nodeLabel} 当前不是本地节点，因此不允许显式绝对路径。`;
}

export function getSelectableSlaveNodes(nodes: NodeSummary[]) {
  const nonImplicitNodes = nodes.filter((node) => !node.isImplicitHostNode);
  return nonImplicitNodes.length > 0 ? nonImplicitNodes : nodes;
}

function normalizeDisplayPath(path: string) {
  return path.replace(/\\/g, '/');
}

function normalizeRelativeSegments(path: string) {
  const normalized = normalizeDisplayPath(path);
  const stack: string[] = [];

  for (const segment of normalized.split('/')) {
    if (!segment || segment === '.') {
      continue;
    }

    if (segment === '..') {
      if (stack.length > 0 && stack[stack.length - 1] !== '..') {
        stack.pop();
      }
      continue;
    }

    stack.push(segment);
  }

  return stack.join('/');
}

function joinDisplayPath(rootPath: string, scopedPath: string) {
  const normalizedRoot = normalizeDisplayPath(rootPath).replace(/\/+$/, '');
  const normalizedPath = normalizeRelativeSegments(scopedPath).replace(/^\/+/, '');
  return `${normalizedRoot}/${normalizedPath}`;
}

export function getResolvedPathDisplay(node: NodeSummary | undefined, configuredPath: string | undefined) {
  const trimmed = configuredPath?.trim();

  if (!trimmed) {
    return node?.rootPath ? normalizeDisplayPath(node.rootPath) : '节点根路径';
  }

  if (isAbsolutePlanPath(trimmed)) {
    return canUseAbsolutePath(node)
      ? normalizeDisplayPath(trimmed)
      : `无效绝对路径：${normalizeDisplayPath(trimmed)}`;
  }

  if (node?.isImplicitHostNode && !node.rootPath) {
    return `工作区/${normalizeRelativeSegments(trimmed)}`;
  }

  return node?.rootPath
    ? joinDisplayPath(node.rootPath, trimmed)
    : normalizeRelativeSegments(trimmed);
}

export const formatResolvedPathDisplay = getResolvedPathDisplay;

export function formatConfiguredAndResolvedPath(node: NodeSummary | undefined, configuredPath: string | undefined) {
  const configured = configuredPath?.trim() || '.';
  return `${configured}（最终：${getResolvedPathDisplay(node, configuredPath)}）`;
}

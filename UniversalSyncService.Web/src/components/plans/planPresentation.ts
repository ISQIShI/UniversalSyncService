import type { NodeSummary } from '../../api.ts';
import { type I18nKey } from '../../i18n/types.ts';
import { translateForCurrentLocale } from '../../i18n/translate.ts';

export const HOST_LOCAL_NODE_ID = 'host-local';

type Translator = (key: I18nKey, params?: Record<string, string | number>) => string;

type OptionWithKey = {
  value: string;
  labelKey: I18nKey;
};

const defaultTranslator: Translator = (key, params) => translateForCurrentLocale(key, params);

export const conflictResolutionOptionKeys: OptionWithKey[] = [
  { value: 'Manual', labelKey: 'web.plans.conflict.manual' },
  { value: 'KeepNewer', labelKey: 'web.plans.conflict.keepNewer' },
  { value: 'KeepLocal', labelKey: 'web.plans.conflict.keepLocal' },
  { value: 'KeepRemote', labelKey: 'web.plans.conflict.keepRemote' },
] as const;

export function getConflictResolutionOptions(t: Translator = defaultTranslator) {
  return conflictResolutionOptionKeys.map((option) => ({
    value: option.value,
    label: t(option.labelKey),
  }));
}

const conflictResolutionDescriptionKeys: Record<string, I18nKey> = {
  Manual: 'web.plans.conflict.description.manual',
  KeepNewer: 'web.plans.conflict.description.keepNewer',
  KeepLocal: 'web.plans.conflict.description.keepLocal',
  KeepRemote: 'web.plans.conflict.description.keepRemote',
};

export function getConflictResolutionDescription(strategy: string | undefined, t: Translator = defaultTranslator) {
  if (!strategy) {
    return t('web.plans.conflict.description.default');
  }

  const key = conflictResolutionDescriptionKeys[strategy] ?? 'web.plans.conflict.description.default';
  return t(key);
}

export const syncModeOptionKeys: OptionWithKey[] = [
  { value: 'Bidirectional', labelKey: 'web.plans.syncMode.bidirectional' },
  { value: 'Push', labelKey: 'web.plans.syncMode.push' },
  { value: 'Pull', labelKey: 'web.plans.syncMode.pull' },
  { value: 'PushAndDelete', labelKey: 'web.plans.syncMode.pushAndDelete' },
  { value: 'PullAndDelete', labelKey: 'web.plans.syncMode.pullAndDelete' },
] as const;

export function getSyncModeOptions(t: Translator = defaultTranslator) {
  return syncModeOptionKeys.map((option) => ({
    value: option.value,
    label: t(option.labelKey),
  }));
}

export const triggerTypeOptionKeys: OptionWithKey[] = [
  { value: 'Manual', labelKey: 'web.plans.trigger.manual' },
  { value: 'Scheduled', labelKey: 'web.plans.trigger.scheduled' },
  { value: 'Realtime', labelKey: 'web.plans.trigger.realtime' },
] as const;

export function getTriggerTypeOptions(t: Translator = defaultTranslator) {
  return triggerTypeOptionKeys.map((option) => ({
    value: option.value,
    label: t(option.labelKey),
  }));
}

export function formatNodeLabel(node: NodeSummary | undefined, fallbackId: string, t: Translator = defaultTranslator) {
  if (!node) {
    return fallbackId;
  }

  if (node.isImplicitHostNode) {
    return t('web.plans.nodeLabel.hostDefault', { name: node.name });
  }

  return t('web.plans.nodeLabel.normal', { name: node.name, id: node.id });
}

export function formatMasterNodeLabel(masterNodeId: string, nodes: NodeSummary[], t: Translator = defaultTranslator) {
  const effectiveNodeId = getEffectiveMasterNodeId(masterNodeId);
  const node = nodes.find((item) => item.id === effectiveNodeId);
  return formatNodeLabel(node, effectiveNodeId, t);
}

export function formatTriggerTypeLabel(triggerType: string, t: Translator = defaultTranslator) {
  const key = triggerTypeOptionKeys.find((option) => option.value === triggerType)?.labelKey;
  return key ? t(key) : triggerType;
}

export function formatSyncModeLabel(syncMode: string, t: Translator = defaultTranslator) {
  const key = syncModeOptionKeys.find((option) => option.value === syncMode)?.labelKey;
  return key ? t(key) : syncMode;
}

export function formatConflictResolutionStrategyLabel(strategy: string | undefined, t: Translator = defaultTranslator) {
  switch (strategy) {
    case 'KeepNewer':
      return t('web.plans.conflict.keepNewer');
    case 'Manual':
    case undefined:
    case '':
      return t('web.plans.conflict.manual');
    case 'KeepLocal':
      return t('web.plans.conflict.keepLocal');
    case 'KeepRemote':
      return t('web.plans.conflict.keepRemote');
    case 'KeepLarger':
      return t('web.plans.conflict.keepLarger');
    case 'RenameBoth':
      return t('web.plans.conflict.renameBoth');
    default:
      return t('web.plans.conflict.unsupported', { strategy });
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

export function getPathRuleHint(node: NodeSummary | undefined, t: Translator = defaultTranslator) {
  if (node?.isImplicitHostNode) {
    return t('web.plans.pathRule.hostDefault');
  }

  if (node?.nodeType?.toLowerCase() === 'local') {
    return t('web.plans.pathRule.local');
  }

  return t('web.plans.pathRule.remote');
}

export function getEffectiveMasterNodeId(masterNodeId: string | undefined) {
  return masterNodeId?.trim() ? masterNodeId : HOST_LOCAL_NODE_ID;
}

export function getNodeById(nodes: NodeSummary[], nodeId: string | undefined) {
  const effectiveNodeId = getEffectiveMasterNodeId(nodeId);
  return nodes.find((node) => node.id === effectiveNodeId);
}

export function formatAbsolutePathValidationError(pathLabelKey: I18nKey, node: NodeSummary | undefined, t: Translator = defaultTranslator) {
  const nodeLabel = node?.isImplicitHostNode
    ? t('web.plans.nodeLabel.hostDefaultNoName')
    : (node?.name ? t('web.plans.nodeLabel.boxedName', { name: node.name }) : t('web.plans.nodeLabel.currentNode'));
  return t('web.plans.validation.absolutePathNotAllowed', {
    pathLabel: t(pathLabelKey),
    nodeLabel,
  });
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

export function getResolvedPathDisplay(node: NodeSummary | undefined, configuredPath: string | undefined, t: Translator = defaultTranslator) {
  const trimmed = configuredPath?.trim();

  if (!trimmed) {
    return node?.rootPath ? normalizeDisplayPath(node.rootPath) : t('web.plans.path.rootPathFallback');
  }

  if (isAbsolutePlanPath(trimmed)) {
    return canUseAbsolutePath(node)
      ? normalizeDisplayPath(trimmed)
      : t('web.plans.path.invalidAbsolute', { path: normalizeDisplayPath(trimmed) });
  }

  if (node?.isImplicitHostNode && !node.rootPath) {
    return t('web.plans.path.workspaceResolved', { path: normalizeRelativeSegments(trimmed) });
  }

  return node?.rootPath
    ? joinDisplayPath(node.rootPath, trimmed)
    : normalizeRelativeSegments(trimmed);
}

export const formatResolvedPathDisplay = getResolvedPathDisplay;

export function formatConfiguredAndResolvedPath(node: NodeSummary | undefined, configuredPath: string | undefined, t: Translator = defaultTranslator) {
  const configured = configuredPath?.trim() || '.';
  return t('web.plans.path.configuredResolved', {
    configured,
    resolved: getResolvedPathDisplay(node, configuredPath, t),
  });
}

export const PlanPresentationHelpers = {
  getEmptyPlanListMessage: () => '无可用同步计划。',
  getNoDescriptionMessage: () => '暂无描述。'
};

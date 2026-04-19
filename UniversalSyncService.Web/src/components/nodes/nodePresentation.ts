import type { NodeDetail } from '../../api.ts';

export type NodeFormState = {
  id: string;
  name: string;
  nodeType: string;
  isEnabled: boolean;
  rootPath: string;
  connectionSettings: Record<string, string>;
  connectionSettingsText: string;
  customOptions: Record<string, string>;
  customOptionsText: string;
};

export function createDefaultNodeForm(defaultName: string = ''): NodeFormState {
  return {
    id: '',
    name: defaultName,
    nodeType: 'Local',
    isEnabled: true,
    rootPath: '',
    connectionSettings: {},
    connectionSettingsText: '',
    customOptions: {},
    customOptionsText: '',
  };
}

export function mapNodeDetailToForm(detail: NodeDetail): NodeFormState {
  return {
    id: detail.id,
    name: detail.name,
    nodeType: detail.nodeType,
    isEnabled: detail.isEnabled,
    rootPath: detail.connectionSettings.RootPath ?? detail.rootPath ?? '',
    connectionSettings: detail.connectionSettings,
    connectionSettingsText: Object.entries(detail.connectionSettings)
      .map(([key, value]) => `${key}=${value}`)
      .join('\n'),
    customOptions: detail.customOptions,
    customOptionsText: Object.entries(detail.customOptions)
      .map(([key, value]) => `${key}=${value}`)
      .join('\n'),
  };
}

export function parseKeyValueText(text: string): Record<string, string> {
  const result: Record<string, string> = {};
  for (const line of text.split(/\r?\n/)) {
    const trimmed = line.trim();
    if (!trimmed) continue;

    const separatorIndex = trimmed.indexOf('=');
    if (separatorIndex <= 0) continue;

    const key = trimmed.slice(0, separatorIndex).trim();
    const value = trimmed.slice(separatorIndex + 1).trim();
    if (key && value) {
      result[key] = value;
    }
  }

  return result;
}

export function serializeKeyValueRecord(record: Record<string, string>): string {
  return Object.entries(record)
    .map(([key, value]) => `${key}=${value}`)
    .join('\n');
}

export function getCaseInsensitiveSetting(settings: Record<string, string>, key: string): string | undefined {
  const matchedEntry = Object.entries(settings).find(([existingKey]) => existingKey.toLowerCase() === key.toLowerCase());
  return matchedEntry?.[1];
}

export function upsertCaseInsensitiveSetting(settings: Record<string, string>, key: string, value: string) {
  const matchedEntry = Object.keys(settings).find((existingKey) => existingKey.toLowerCase() === key.toLowerCase());
  if (matchedEntry) {
    settings[matchedEntry] = value;
    return;
  }

  settings[key] = value;
}

export const NodePresentationHelpers = {
  getLoadDetailFailedMessage: () => '加载节点详情失败。',
  getEmptyNodeIdMessage: () => '必须提供节点 ID。',
  getEmptyNodeNameMessage: () => '必须提供节点名称。',
  getLocalNodeRootPathRequiredMessage: () => '本地节点必须提供根路径。',
  getOneDriveRootPathRequiredMessage: () => 'OneDrive 节点必须提供根路径，如 / 或是 /Apps/UniversalSyncService。',
  getNodeCreatedMessage: () => '节点已创建。',
  getNodeUpdatedMessage: () => '节点已更新。',
  getSaveNodeFailedMessage: () => '保存节点失败。',
  getConfirmDeleteNodeMessage: (name: string) => `确定要删除节点 "${name}" 吗？`,
  getNodeDeletedMessage: () => '节点已删除。',
  getDeleteNodeFailedMessage: () => '删除节点失败。',
  getNodesPageSubtitle: () => '管理同步节点以及宿主默认节点',
  getCreateNodeTitle: () => '创建节点',
  getImplicitNodeDescription: () => '此节点由宿主运行时自动提供，为只读节点。',
  getEmptyStateMessage: () => '请选择一个节点查看详情。',
  getNewNodeDefaultName: () => '新节点',
  getEmptyNodeListMessage: () => '暂无节点配置。',
  getSelectLeftNodeMessage: () => '请在左侧选择节点。',
  getNodeDetailTitle: () => '节点详情'
};

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

export function createDefaultNodeForm(): NodeFormState {
  return {
    id: '',
    name: '新节点',
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

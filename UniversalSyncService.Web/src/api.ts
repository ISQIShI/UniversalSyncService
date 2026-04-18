export type ServiceStatus = {
  serviceName: string;
  startedAt: string;
  uptimeSeconds: number;
  planCount: number;
  activeTaskCount: number;
  nodeCount: number;
  loadedPluginCount: number;
};

export type PlanSummary = {
  id: string;
  name: string;
  description: string;
  isEnabled: boolean;
  masterNodeId: string;
  syncItemType: string;
  executionCount: number;
  lastExecutedAt?: string;
};

export type PlanDetail = PlanSummary & {
  triggerType: string;
  cronExpression?: string;
  intervalSeconds?: number;
  enableFileSystemWatcher: boolean;
  slaves: Array<{
    slaveNodeId: string;
    syncMode: string;
    sourcePath?: string;
    targetPath?: string;
    enableDeletionProtection: boolean;
    conflictResolutionStrategy: string;
    filters?: string[];
    exclusions?: string[];
  }>;
};

export type CreateOrUpdatePlanRequest = {
  name: string;
  description: string;
  isEnabled: boolean;
  masterNodeId: string;
  syncItemType: string;
  triggerType: string;
  cronExpression?: string;
  intervalSeconds?: number;
  enableFileSystemWatcher: boolean;
    slaves: Array<{
      slaveNodeId: string;
      syncMode: string;
      sourcePath?: string;
      targetPath?: string;
      enableDeletionProtection: boolean;
      conflictResolutionStrategy?: string;
      filters?: string[];
      exclusions?: string[];
    }>;
};

export type HistoryEntry = {
  id: string;
  planId: string;
  taskId: string;
  nodeId: string;
  path: string;
  name: string;
  size: number;
  state: string;
  syncTimestamp: string;
  syncVersion: number;
  checksum?: string;
};

export type NodeSummary = {
  id: string;
  name: string;
  nodeType: string;
  isEnabled: boolean;
  isImplicitHostNode: boolean;
  rootPath?: string;
  sourceLabel: string;
};

export type NodeDetail = NodeSummary & {
  connectionSettings: Record<string, string>;
  customOptions: Record<string, string>;
  createdAt: string;
  modifiedAt: string;
};

export type CreateOrUpdateNodeRequest = {
  id?: string;
  name: string;
  nodeType: string;
  isEnabled: boolean;
  connectionSettings: Record<string, string>;
  customOptions: Record<string, string>;
};

export type PluginSummary = {
  id: string;
  name: string;
  version: string;
  description: string;
};

export type ConfigSummary = {
  serviceName: string;
  configurationFilePath: string;
  enableSyncFramework: boolean;
  historyStorePath: string;
  nodeCount: number;
  planCount: number;
  enablePluginSystem: boolean;
  pluginDirectory: string;
};

export type OneDriveDefaults = {
  isConfigured: boolean;
  clientId?: string;
  tenantId?: string;
};

export type ExecutePlanResponse = {
  planId: string;
  totalTasks: number;
  successCount: number;
  noChangesCount: number;
  conflictCount: number;
  failedCount: number;
};

export type PublicInterfaceProfile = {
  enableWebConsole: boolean;
  enableHttpApi: boolean;
  enableGrpc: boolean;
  requireManagementApiKey: boolean;
};

const jsonHeaders = (apiKey: string) => {
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
  };

  if (apiKey.trim()) {
    headers.Authorization = `Bearer ${apiKey}`;
  }

  return headers;
};

export class ApiRequestError extends Error {
  constructor(message: string, public readonly status: number) {
    super(message);
    this.name = 'ApiRequestError';
  }
}

async function requestJson<T>(path: string, apiKey: string, method = 'GET', body?: unknown): Promise<T> {
  const options: RequestInit = {
    method,
    headers: jsonHeaders(apiKey),
  };
  
  if (body) {
    options.body = JSON.stringify(body);
  }

  const response = await fetch(path, options);

  if (!response.ok) {
    const text = await response.text();
    if (text) {
      try {
        const parsed = JSON.parse(text) as { error?: string };
        throw new ApiRequestError(parsed.error || text, response.status);
      } catch {
        throw new ApiRequestError(text, response.status);
      }
    }

    throw new ApiRequestError(`请求失败：${response.status}`, response.status);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  const text = await response.text();
  if (!text) {
    return undefined as T;
  }

  return JSON.parse(text) as T;
}

export async function getHealth(): Promise<{ status: string }> {
  const response = await fetch('/health');
  if (!response.ok) {
    throw new Error(`健康检查失败：${response.status}`);
  }

  return response.json() as Promise<{ status: string }>;
}

export async function getPublicInterfaceProfile(): Promise<PublicInterfaceProfile> {
  const response = await fetch('/api/public/interface-profile');
  if (!response.ok) {
    throw new Error(`接口配置获取失败：${response.status}`);
  }

  return response.json() as Promise<PublicInterfaceProfile>;
}

export function getStatus(apiKey: string) {
  return requestJson<ServiceStatus>('/api/v1/status', apiKey);
}

export function getOneDriveDefaults(apiKey: string) {
  return requestJson<OneDriveDefaults>('/api/v1/config/onedrive-defaults', apiKey);
}

export function getPlans(apiKey: string) {
  return requestJson<PlanSummary[]>('/api/v1/plans', apiKey);
}

export function getPlanDetail(planId: string, apiKey: string) {
  return requestJson<PlanDetail>(`/api/v1/plans/${encodeURIComponent(planId)}`, apiKey);
}

export function getPlanHistory(planId: string, apiKey: string, limit = 20) {
  return requestJson<HistoryEntry[]>(`/api/v1/plans/${encodeURIComponent(planId)}/history?limit=${limit}`, apiKey);
}

export function getNodes(apiKey: string) {
  return requestJson<NodeSummary[]>('/api/v1/nodes', apiKey);
}

export function getNodeDetail(nodeId: string, apiKey: string) {
  return requestJson<NodeDetail>(`/api/v1/nodes/${encodeURIComponent(nodeId)}`, apiKey);
}

export function createNode(node: CreateOrUpdateNodeRequest, apiKey: string) {
  return requestJson<NodeDetail>('/api/v1/nodes', apiKey, 'POST', node);
}

export function updateNode(nodeId: string, node: CreateOrUpdateNodeRequest, apiKey: string) {
  return requestJson<NodeDetail>(`/api/v1/nodes/${encodeURIComponent(nodeId)}`, apiKey, 'PUT', node);
}

export function deleteNode(nodeId: string, apiKey: string) {
  return requestJson<void>(`/api/v1/nodes/${encodeURIComponent(nodeId)}`, apiKey, 'DELETE');
}

export function getPlugins(apiKey: string) {
  return requestJson<PluginSummary[]>('/api/v1/plugins', apiKey);
}

export function getGlobalHistory(apiKey: string, limit = 20) {
  return requestJson<HistoryEntry[]>(`/api/v1/history?limit=${limit}`, apiKey);
}

export function getConfigSummary(apiKey: string) {
  return requestJson<ConfigSummary>('/api/v1/config/summary', apiKey);
}

export function executePlan(planId: string, apiKey: string) {
  return requestJson<ExecutePlanResponse>(`/api/v1/plans/${encodeURIComponent(planId)}/execute-now`, apiKey, 'POST');
}

export function createPlan(plan: CreateOrUpdatePlanRequest, apiKey: string) {
  return requestJson<PlanDetail>('/api/v1/plans', apiKey, 'POST', plan);
}

export function updatePlan(planId: string, plan: CreateOrUpdatePlanRequest, apiKey: string) {
  return requestJson<PlanDetail>(`/api/v1/plans/${encodeURIComponent(planId)}`, apiKey, 'PUT', plan);
}

export function deletePlan(planId: string, apiKey: string) {
  return requestJson<void>(`/api/v1/plans/${encodeURIComponent(planId)}`, apiKey, 'DELETE');
}




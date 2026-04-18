import { useEffect, useState } from 'react';
import { createNode, deleteNode, getNodeDetail, getOneDriveDefaults, updateNode, type NodeDetail, type OneDriveDefaults } from '../api.ts';
import { MessageBanner } from '../components/common/MessageBanner.tsx';
import { Panel } from '../components/common/Panel.tsx';
import { NodeDetailView } from '../components/nodes/NodeDetail.tsx';
import { NodeForm, toNodeRequestPayload } from '../components/nodes/NodeForm.tsx';
import { NodeList } from '../components/nodes/NodeList.tsx';
import { createDefaultNodeForm, mapNodeDetailToForm, type NodeFormState } from '../components/nodes/nodePresentation.ts';
import { useAppStore } from '../store/useAppStore.ts';

export function NodesPage() {
  const { apiKey, nodes, isConnected, canUseAnonymousApi, fetchConsoleState } = useAppStore();
  const canManage = canUseAnonymousApi || isConnected;
  const apiCredential = apiKey || '';

  const [selectedNodeId, setSelectedNodeId] = useState<string | null>(null);
  const [selectedNode, setSelectedNode] = useState<NodeDetail | null>(null);
  const [isCreating, setIsCreating] = useState(false);
  const [isEditing, setIsEditing] = useState(false);
  const [formData, setFormData] = useState<NodeFormState | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [errorMsg, setErrorMsg] = useState('');
  const [successMsg, setSuccessMsg] = useState('');
  const [oneDriveDefaults, setOneDriveDefaults] = useState<OneDriveDefaults | null>(null);

  useEffect(() => {
    if (!canManage) {
      return;
    }

    void getOneDriveDefaults(apiCredential)
      .then((defaults) => setOneDriveDefaults(defaults))
      .catch(() => setOneDriveDefaults(null));
  }, [apiCredential, canManage]);

  useEffect(() => {
    if (!formData || formData.nodeType.trim().toLowerCase() !== 'onedrive' || !oneDriveDefaults?.isConfigured) {
      return;
    }

    setFormData((current) => {
      if (!current || current.nodeType.trim().toLowerCase() !== 'onedrive') {
        return current;
      }

      const nextConnectionSettings = parseOneDriveConnectionSettings(current.connectionSettingsText);
      let changed = false;

      if (oneDriveDefaults.clientId && !getCaseInsensitiveSetting(nextConnectionSettings, 'ClientId')) {
        upsertCaseInsensitiveSetting(nextConnectionSettings, 'ClientId', oneDriveDefaults.clientId);
        changed = true;
      }

      if (oneDriveDefaults.tenantId && !getCaseInsensitiveSetting(nextConnectionSettings, 'TenantId')) {
        upsertCaseInsensitiveSetting(nextConnectionSettings, 'TenantId', oneDriveDefaults.tenantId);
        changed = true;
      }

      if (!getCaseInsensitiveSetting(nextConnectionSettings, 'AuthMode')) {
        upsertCaseInsensitiveSetting(nextConnectionSettings, 'AuthMode', 'InteractiveBrowser');
        changed = true;
      }

      if (!changed) {
        return current;
      }

      return {
        ...current,
        connectionSettingsText: Object.entries(nextConnectionSettings).map(([key, value]) => `${key}=${value}`).join('\n'),
      };
    });
  }, [formData, oneDriveDefaults]);

  useEffect(() => {
    if (!selectedNodeId && nodes.length > 0) {
      setSelectedNodeId(nodes[0].id);
    }
  }, [nodes, selectedNodeId]);

  useEffect(() => {
    if (!selectedNodeId) {
      setSelectedNode(null);
      return;
    }

    void getNodeDetail(selectedNodeId, apiCredential)
      .then((detail) => setSelectedNode(detail))
      .catch((error) => setErrorMsg(error instanceof Error ? error.message : '加载节点详情失败。'));
  }, [apiCredential, selectedNodeId]);

  function resetMessages() {
    setErrorMsg('');
    setSuccessMsg('');
  }

  function startCreate() {
    resetMessages();
    setIsCreating(true);
    setIsEditing(false);
    setFormData(createDefaultNodeForm());
  }

  function startEdit() {
    if (!selectedNode || selectedNode.isImplicitHostNode) {
      return;
    }

    resetMessages();
    setIsEditing(true);
    setIsCreating(false);
    setFormData(mapNodeDetailToForm(selectedNode));
  }

  function cancelEditing() {
    setIsCreating(false);
    setIsEditing(false);
    setFormData(null);
    resetMessages();
  }

  async function handleSubmit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!formData || !canManage) {
      return;
    }

    if (!formData.id.trim() && isCreating) {
      setErrorMsg('节点 ID 不能为空。');
      return;
    }

    if (!formData.name.trim()) {
      setErrorMsg('节点名称不能为空。');
      return;
    }

    if (formData.nodeType.trim() === 'Local' && !formData.rootPath.trim()) {
      setErrorMsg('本地节点必须提供根路径。');
      return;
    }

    if (formData.nodeType.trim() === 'OneDrive' && !formData.rootPath.trim()) {
      setErrorMsg('OneDrive 节点必须提供根路径，请填写 / 或 /Apps/UniversalSyncService。');
      return;
    }

    try {
      setSubmitting(true);
      resetMessages();
      const payload = toNodeRequestPayload(formData);

      if (isCreating) {
        const created = await createNode(payload, apiCredential);
        setSuccessMsg('节点已创建。');
        setIsCreating(false);
        fetchConsoleState();
        setSelectedNodeId(created.id);
      } else {
        const updated = await updateNode(formData.id, payload, apiCredential);
        setSuccessMsg('节点已更新。');
        setIsEditing(false);
        fetchConsoleState();
        setSelectedNodeId(updated.id);
      }
    } catch (error) {
      setErrorMsg(error instanceof Error ? error.message : '保存节点失败。');
    } finally {
      setSubmitting(false);
    }
  }

  async function handleDelete() {
    if (!selectedNode || selectedNode.isImplicitHostNode || !canManage) {
      return;
    }

    if (!window.confirm(`确认删除节点「${selectedNode.name}」吗？`)) {
      return;
    }

    try {
      setSubmitting(true);
      resetMessages();
      await deleteNode(selectedNode.id, apiCredential);
      setSuccessMsg('节点已删除。');
      setSelectedNodeId(null);
      setSelectedNode(null);
      fetchConsoleState();
    } catch (error) {
      setErrorMsg(error instanceof Error ? error.message : '删除节点失败。');
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="page-grid plans-grid">
      <Panel title="节点管理" subtitle="管理同步节点与宿主默认节点" actions={<button className="primary" type="button" onClick={startCreate} disabled={!canManage || isCreating}>新增节点</button>}>
        <NodeList
          nodes={nodes}
          selectedNodeId={selectedNodeId}
          isCreating={isCreating}
          onSelectNode={(nodeId) => {
            setSelectedNodeId(nodeId);
            setIsCreating(false);
            setIsEditing(false);
            resetMessages();
          }}
        />
      </Panel>

      <Panel
        title={isCreating ? '新增节点' : isEditing ? `编辑节点：${formData?.name}` : selectedNode?.name ?? '节点详情'}
        subtitle={isCreating ? '创建新的同步节点' : selectedNode?.isImplicitHostNode ? '该节点由宿主运行时自动提供，仅可查看。' : selectedNode ? `节点 ID：${selectedNode.id}` : '请选择左侧节点。'}
        preserveTitleCase={Boolean((isEditing && formData?.name) || (!isCreating && !isEditing && selectedNode))}
        actions={selectedNode && !isCreating && !isEditing ? (
          <div className="plan-detail-actions">
            <button type="button" onClick={startEdit} disabled={!canManage || selectedNode.isImplicitHostNode}>编辑</button>
            <button type="button" className="danger-text" onClick={handleDelete} disabled={!canManage || selectedNode.isImplicitHostNode || submitting}>删除</button>
          </div>
        ) : undefined}>
        {errorMsg ? <MessageBanner tone="error" message={errorMsg} inline /> : null}
        {successMsg ? <MessageBanner tone="success" message={successMsg} inline /> : null}

        {isCreating || isEditing ? (
          formData ? <NodeForm formData={formData} submitting={submitting} isEditing={isEditing} onSubmit={handleSubmit} onCancel={cancelEditing} setFormData={setFormData} /> : null
        ) : selectedNode ? (
          <NodeDetailView selectedNode={selectedNode} />
        ) : (
          <div className="empty-state editorial-empty" style={{ margin: 'var(--space-6)' }}>请选择一个节点查看详情。</div>
        )}
      </Panel>
    </div>
  );
}

function parseOneDriveConnectionSettings(text: string): Record<string, string> {
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

function getCaseInsensitiveSetting(settings: Record<string, string>, key: string): string | undefined {
  const matchedEntry = Object.entries(settings).find(([existingKey]) => existingKey.toLowerCase() === key.toLowerCase());
  return matchedEntry?.[1];
}

function upsertCaseInsensitiveSetting(settings: Record<string, string>, key: string, value: string) {
  const matchedEntry = Object.keys(settings).find((existingKey) => existingKey.toLowerCase() === key.toLowerCase());
  if (matchedEntry) {
    settings[matchedEntry] = value;
    return;
  }

  settings[key] = value;
}

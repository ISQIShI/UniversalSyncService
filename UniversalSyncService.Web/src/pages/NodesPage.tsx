import { useEffect, useState } from 'react';
import { createNode, deleteNode, getNodeDetail, getOneDriveDefaults, updateNode, type NodeDetail, type OneDriveDefaults } from '../api.ts';
import { MessageBanner } from '../components/common/MessageBanner.tsx';
import { Panel } from '../components/common/Panel.tsx';
import { NodeDetailView } from '../components/nodes/NodeDetail.tsx';
import { NodeForm, toNodeRequestPayload } from '../components/nodes/NodeForm.tsx';
import { NodeList } from '../components/nodes/NodeList.tsx';
import { createDefaultNodeForm, mapNodeDetailToForm, NodePresentationHelpers, parseKeyValueText, getCaseInsensitiveSetting, upsertCaseInsensitiveSetting, type NodeFormState } from '../components/nodes/nodePresentation.ts';
import { useAppStore } from '../store/useAppStore.ts';
import { useI18n } from '../i18n/useI18n.ts';

export function NodesPage() {
  const { apiKey, nodes, isConnected, canUseAnonymousApi, fetchConsoleState } = useAppStore();
  const { t } = useI18n();
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

      const nextConnectionSettings = parseKeyValueText(current.connectionSettingsText);
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
      .catch((error) => setErrorMsg(error instanceof Error ? error.message : NodePresentationHelpers.getLoadDetailFailedMessage()));
  }, [apiCredential, selectedNodeId]);

  function resetMessages() {
    setErrorMsg('');
    setSuccessMsg('');
  }

  function startCreate() {
    resetMessages();
    setIsCreating(true);
    setIsEditing(false);
    setFormData(createDefaultNodeForm(NodePresentationHelpers.getNewNodeDefaultName()));
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
      setErrorMsg(NodePresentationHelpers.getEmptyNodeIdMessage());
      return;
    }

    if (!formData.name.trim()) {
      setErrorMsg(NodePresentationHelpers.getEmptyNodeNameMessage());
      return;
    }

    if (formData.nodeType.trim() === 'Local' && !formData.rootPath.trim()) {
      setErrorMsg(NodePresentationHelpers.getLocalNodeRootPathRequiredMessage());
      return;
    }

    if (formData.nodeType.trim() === 'OneDrive' && !formData.rootPath.trim()) {
      setErrorMsg(NodePresentationHelpers.getOneDriveRootPathRequiredMessage());
      return;
    }

    try {
      setSubmitting(true);
      resetMessages();
      const payload = toNodeRequestPayload(formData);

      if (isCreating) {
        const created = await createNode(payload, apiCredential);
        setSuccessMsg(NodePresentationHelpers.getNodeCreatedMessage());
        setIsCreating(false);
        fetchConsoleState();
        setSelectedNodeId(created.id);
      } else {
        const updated = await updateNode(formData.id, payload, apiCredential);
        setSuccessMsg(NodePresentationHelpers.getNodeUpdatedMessage());
        setIsEditing(false);
        fetchConsoleState();
        setSelectedNodeId(updated.id);
      }
    } catch (error) {
      setErrorMsg(error instanceof Error ? error.message : NodePresentationHelpers.getSaveNodeFailedMessage());
    } finally {
      setSubmitting(false);
    }
  }

  async function handleDelete() {
    if (!selectedNode || selectedNode.isImplicitHostNode || !canManage) {
      return;
    }

    if (!window.confirm(NodePresentationHelpers.getConfirmDeleteNodeMessage(selectedNode.name))) {
      return;
    }

    try {
      setSubmitting(true);
      resetMessages();
      await deleteNode(selectedNode.id, apiCredential);
      setSuccessMsg(NodePresentationHelpers.getNodeDeletedMessage());
      setSelectedNodeId(null);
      setSelectedNode(null);
      fetchConsoleState();
    } catch (error) {
      setErrorMsg(error instanceof Error ? error.message : NodePresentationHelpers.getDeleteNodeFailedMessage());
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="page-grid plans-grid">
      <Panel title={t('web.nav.nodes')} subtitle={NodePresentationHelpers.getNodesPageSubtitle()} actions={<button className="primary" type="button" onClick={startCreate} disabled={!canManage || isCreating}>{NodePresentationHelpers.getCreateNodeTitle()}</button>}>
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
        title={isCreating ? NodePresentationHelpers.getCreateNodeTitle() : isEditing ? `${t('web.actions.edit')}：${formData?.name}` : selectedNode?.name ?? NodePresentationHelpers.getNodeDetailTitle()}
        subtitle={isCreating ? t('web.nodes.create') : selectedNode?.isImplicitHostNode ? NodePresentationHelpers.getImplicitNodeDescription() : selectedNode ? `ID: ${selectedNode.id}` : NodePresentationHelpers.getSelectLeftNodeMessage()}
        preserveTitleCase={Boolean((isEditing && formData?.name) || (!isCreating && !isEditing && selectedNode))}
        actions={selectedNode && !isCreating && !isEditing ? (
          <div className="plan-detail-actions">
            <button type="button" onClick={startEdit} disabled={!canManage || selectedNode.isImplicitHostNode}>{t('web.actions.edit')}</button>
            <button type="button" className="danger-text" onClick={handleDelete} disabled={!canManage || selectedNode.isImplicitHostNode || submitting}>{t('web.actions.delete')}</button>
          </div>
        ) : undefined}>
        {errorMsg ? <MessageBanner tone="error" message={errorMsg} inline /> : null}
        {successMsg ? <MessageBanner tone="success" message={successMsg} inline /> : null}

        {isCreating || isEditing ? (
          formData ? <NodeForm formData={formData} submitting={submitting} isEditing={isEditing} onSubmit={handleSubmit} onCancel={cancelEditing} setFormData={setFormData} /> : null
        ) : selectedNode ? (
          <NodeDetailView selectedNode={selectedNode} />
        ) : (
          <div className="empty-state editorial-empty" style={{ margin: 'var(--space-6)' }}>{NodePresentationHelpers.getEmptyStateMessage()}</div>
        )}
      </Panel>
    </div>
  );
}

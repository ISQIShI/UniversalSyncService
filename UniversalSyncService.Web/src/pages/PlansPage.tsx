import { useEffect, useRef, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { createPlan, deletePlan, updatePlan, getPlanDetail, getPlanHistory, type CreateOrUpdatePlanRequest, type HistoryEntry, type NodeSummary, type PlanDetail } from '../api.ts';
import { Panel } from '../components/common/Panel.tsx';
import { MessageBanner } from '../components/common/MessageBanner.tsx';
import { PlanDetailView } from '../components/plans/PlanDetail.tsx';
import { PlanForm, type PlanFormState } from '../components/plans/PlanForm.tsx';
import { PlanList } from '../components/plans/PlanList.tsx';
import { canUseAbsolutePath, formatAbsolutePathValidationError, getEffectiveMasterNodeId, getNodeById, getSelectableSlaveNodes, isAbsolutePlanPath } from '../components/plans/planPresentation.ts';
import { useI18n } from '../i18n/useI18n.ts';
import { useAppStore } from '../store/useAppStore.ts';

function createDefaultForm(nodes: NodeSummary[], defaultName: string): PlanFormState {
  const defaultSlaveNode = getSelectableSlaveNodes(nodes)[0];
  return {
    id: '',
    name: defaultName,
    description: '',
    isEnabled: true,
    masterNodeId: '',
    syncItemType: 'FileSystem',
    triggerType: 'Manual',
    cronExpression: '',
    intervalSeconds: undefined,
    enableFileSystemWatcher: false,
    slaves: [
      {
        slaveNodeId: defaultSlaveNode?.id ?? '',
        syncMode: 'Bidirectional',
        sourcePath: '.',
        targetPath: '.',
        enableDeletionProtection: true,
        conflictResolutionStrategy: 'Manual',
        filters: [],
        exclusions: [],
      },
    ],
  };
}

function mapPlanToForm(plan: PlanDetail): PlanFormState {
  return {
    id: plan.id,
    name: plan.name,
    description: plan.description,
    isEnabled: plan.isEnabled,
    masterNodeId: plan.masterNodeId,
    syncItemType: plan.syncItemType,
    triggerType: plan.triggerType,
    cronExpression: plan.cronExpression ?? '',
    intervalSeconds: plan.intervalSeconds,
    enableFileSystemWatcher: plan.enableFileSystemWatcher,
    slaves: plan.slaves.map((slave) => ({
      slaveNodeId: slave.slaveNodeId,
      syncMode: slave.syncMode,
      sourcePath: slave.sourcePath ?? '.',
      targetPath: slave.targetPath ?? '.',
      enableDeletionProtection: slave.enableDeletionProtection,
      conflictResolutionStrategy: slave.conflictResolutionStrategy,
      filters: [...(slave.filters ?? [])],
      exclusions: [...(slave.exclusions ?? [])],
    })),
  };
}

function toRequestPayload(formData: PlanFormState): CreateOrUpdatePlanRequest {
  return {
    name: formData.name.trim(),
    description: formData.description.trim(),
    isEnabled: formData.isEnabled,
    masterNodeId: formData.masterNodeId.trim(),
    syncItemType: formData.syncItemType.trim(),
    triggerType: formData.triggerType,
    cronExpression: formData.triggerType === 'Scheduled' ? formData.cronExpression?.trim() || undefined : undefined,
    intervalSeconds: formData.intervalSeconds && formData.intervalSeconds > 0 ? formData.intervalSeconds : undefined,
    enableFileSystemWatcher: formData.enableFileSystemWatcher,
    slaves: formData.slaves.map((slave) => ({
      slaveNodeId: slave.slaveNodeId,
      syncMode: slave.syncMode,
      sourcePath: slave.sourcePath?.trim() || undefined,
      targetPath: slave.targetPath?.trim() || undefined,
      enableDeletionProtection: slave.enableDeletionProtection,
      conflictResolutionStrategy: slave.conflictResolutionStrategy || 'Manual',
      filters: slave.filters?.filter(Boolean) ?? [],
      exclusions: slave.exclusions?.filter(Boolean) ?? [],
    })),
  };
}

function parseCommaSeparatedList(value: string): string[] {
  return value
    .split(',')
    .map((item) => item.trim())
    .filter((item) => item.length > 0);
}

export function PlansPage() {
  const { planId: routePlanId } = useParams();
  const navigate = useNavigate();
  const {
    apiKey,
    plans,
    nodes,
    isBusy: storeIsBusy,
    isConnected,
    canUseAnonymousApi,
    fetchConsoleState,
    executePlanNow,
  } = useAppStore();

  const canExecute = canUseAnonymousApi || isConnected;
  const canManage = canUseAnonymousApi || isConnected;
  const apiCredential = apiKey || '';
  const { t } = useI18n();

  const [selectedPlanId, setSelectedPlanId] = useState<string | null>(routePlanId || (plans.length > 0 ? plans[0].id : null));
  const [selectedPlan, setSelectedPlan] = useState<PlanDetail | null>(null);
  const [history, setHistory] = useState<HistoryEntry[]>([]);
  const [isEditing, setIsEditing] = useState(false);
  const [isCreating, setIsCreating] = useState(false);
  const [formData, setFormData] = useState<PlanFormState | null>(null);
  const [errorMsg, setErrorMsg] = useState('');
  const [successMsg, setSuccessMsg] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const planLoadSequenceRef = useRef(0);

  useEffect(() => {
    if (routePlanId && routePlanId !== selectedPlanId) {
      setSelectedPlanId(routePlanId);
      setIsCreating(false);
      setIsEditing(false);
      setErrorMsg('');
      setSuccessMsg('');
    }
  }, [routePlanId, selectedPlanId]);

  useEffect(() => {
    if (routePlanId) {
      return;
    }

    if (!selectedPlanId && plans.length > 0 && !isCreating) {
      setSelectedPlanId(plans[0].id);
    }
  }, [routePlanId, selectedPlanId, plans, isCreating]);

  useEffect(() => {
    if (!apiCredential && !canUseAnonymousApi) {
      return;
    }

    if (!selectedPlanId) {
      setSelectedPlan(null);
      setHistory([]);
      return;
    }

    const requestSequence = ++planLoadSequenceRef.current;

    let isMounted = true;
    async function loadDetail() {
      try {
        const [planDetail, planHistory] = await Promise.all([
          getPlanDetail(selectedPlanId, apiCredential),
          getPlanHistory(selectedPlanId, apiCredential, 20),
        ]);

        if (isMounted && requestSequence === planLoadSequenceRef.current) {
          setSelectedPlan(planDetail);
          setHistory(planHistory);
        }
      } catch (error) {
        if (isMounted && requestSequence === planLoadSequenceRef.current) {
          setSelectedPlan(null);
          setHistory([]);
          setSelectedPlanId(null);
          setErrorMsg(error instanceof Error ? error.message : t('web.plans.messages.loadDetailFailed'));
          if (routePlanId === selectedPlanId) {
            navigate('/plans', { replace: true });
          }
        }
      }
    }

    void loadDetail();
    return () => {
      isMounted = false;
    };
  }, [selectedPlanId, apiCredential, canUseAnonymousApi, navigate, routePlanId, t]);

  useEffect(() => {
    if (!isEditing && !isCreating) {
      setFormData(null);
    }
  }, [isEditing, isCreating]);

  function resetMessages() {
    setErrorMsg('');
    setSuccessMsg('');
  }

  function handleCreateNew() {
    resetMessages();
    setIsCreating(true);
    setIsEditing(false);
    setSelectedPlanId(null);
    setSelectedPlan(null);
    setHistory([]);
    navigate('/plans', { replace: true });
    setFormData(createDefaultForm(nodes, t('web.plans.form.defaultPlanName')));
  }

  function handleEdit() {
    if (!selectedPlan) {
      return;
    }

    resetMessages();
    setIsEditing(true);
    setIsCreating(false);
    setFormData(mapPlanToForm(selectedPlan));
  }

  async function handleDelete() {
    if (!selectedPlan || !canManage || !window.confirm(t('web.plans.messages.confirmDelete', { name: selectedPlan.name }))) {
      return;
    }

    try {
      setSubmitting(true);
      resetMessages();
      await deletePlan(selectedPlan.id, apiCredential);
      setSuccessMsg(t('web.plans.messages.deleted'));
      setSelectedPlanId(null);
      setSelectedPlan(null);
      setHistory([]);
      navigate('/plans', { replace: true });
      fetchConsoleState();
    } catch (error) {
      setErrorMsg(error instanceof Error ? error.message : t('web.plans.messages.deleteFailed'));
    } finally {
      setSubmitting(false);
    }
  }

  function handleCancel() {
    setIsCreating(false);
    setIsEditing(false);
    setFormData(null);
    resetMessages();

    if (!selectedPlanId) {
      navigate('/plans', { replace: true });
    }
  }

  async function handleExecutePlan(planId: string) {
    if (!canExecute) {
      return;
    }

    try {
      setSubmitting(true);
      resetMessages();
      await executePlanNow(planId);
      setSuccessMsg(t('web.plans.messages.executeTriggered'));

      await new Promise((resolve) => setTimeout(resolve, 1000));

      const [planDetail, planHistory] = await Promise.all([
        getPlanDetail(planId, apiCredential),
        getPlanHistory(planId, apiCredential, 20),
      ]);
      setSelectedPlan(planDetail);
      setHistory(planHistory);
    } catch (error) {
      setErrorMsg(error instanceof Error ? error.message : t('web.plans.messages.executeFailed'));
    } finally {
      setSubmitting(false);
    }
  }

  function updateForm(updater: (current: PlanFormState) => PlanFormState) {
    setFormData((current) => (current ? updater(current) : current));
  }

  function updateSlave(index: number, updater: (current: PlanFormState['slaves'][number]) => PlanFormState['slaves'][number]) {
    updateForm((current) => ({
      ...current,
      slaves: current.slaves.map((slave, slaveIndex) => (slaveIndex === index ? updater(slave) : slave)),
    }));
  }

  function addSlave() {
    const defaultSlaveNode = getSelectableSlaveNodes(nodes)[0];
    updateForm((current) => ({
      ...current,
      slaves: [
        ...current.slaves,
        {
          slaveNodeId: defaultSlaveNode?.id ?? '',
          syncMode: 'Bidirectional',
          sourcePath: '.',
          targetPath: '.',
          enableDeletionProtection: true,
          conflictResolutionStrategy: 'Manual',
          filters: [],
          exclusions: [],
        },
      ],
    }));
  }

  function removeSlave(index: number) {
    updateForm((current) => ({
      ...current,
      slaves: current.slaves.filter((_, slaveIndex) => slaveIndex !== index),
    }));
  }

  async function handleSubmit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!formData || !canManage) {
      return;
    }

    if (!formData.name.trim()) {
      setErrorMsg(t('web.plans.validation.planNameRequired'));
      return;
    }

    if (!formData.syncItemType.trim()) {
      setErrorMsg(t('web.plans.validation.syncItemTypeRequired'));
      return;
    }

    if (formData.slaves.length === 0) {
      setErrorMsg(t('web.plans.validation.slaveRequired'));
      return;
    }

    if (formData.triggerType === 'Scheduled' && !formData.cronExpression?.trim() && !formData.intervalSeconds) {
      setErrorMsg(t('web.plans.validation.scheduleRequired'));
      return;
    }

    const selectedMasterNode = getNodeById(nodes, getEffectiveMasterNodeId(formData.masterNodeId));
    for (const slave of formData.slaves) {
      const selectedSlaveNode = nodes.find((node) => node.id === slave.slaveNodeId);

      if (isAbsolutePlanPath(slave.targetPath) && !canUseAbsolutePath(selectedMasterNode)) {
        setErrorMsg(formatAbsolutePathValidationError('web.plans.masterNodePath', selectedMasterNode, t));
        return;
      }

      if (isAbsolutePlanPath(slave.sourcePath) && !canUseAbsolutePath(selectedSlaveNode)) {
        setErrorMsg(formatAbsolutePathValidationError('web.plans.slaveNodePath', selectedSlaveNode, t));
        return;
      }
    }

    try {
      setSubmitting(true);
      resetMessages();
      const payload = toRequestPayload(formData);

      if (isCreating) {
        const created = await createPlan(payload, apiCredential);
        setSuccessMsg(t('web.plans.messages.created'));
        setIsCreating(false);
        await fetchConsoleState();
        setSelectedPlanId(created.id);
        navigate(`/plans/${created.id}`, { replace: true });
      } else {
        const updated = await updatePlan(formData.id, payload, apiCredential);
        setSuccessMsg(t('web.plans.messages.updated'));
        setIsEditing(false);
        await fetchConsoleState();
        setSelectedPlanId(updated.id);
        navigate(`/plans/${updated.id}`, { replace: true });
      }
    } catch (error) {
      setErrorMsg(error instanceof Error ? error.message : t('web.plans.messages.saveFailed'));
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="page-grid plans-grid">
      <Panel title={t('web.plans.page.title')} subtitle={t('web.plans.page.subtitle')} actions={<button type="button" className="primary" onClick={handleCreateNew} disabled={!canManage || isCreating}>{t('web.plans.create')}</button>}>
        <PlanList
          plans={plans}
          selectedPlanId={selectedPlanId}
          isCreating={isCreating}
          onSelectPlan={(planId) => {
            setSelectedPlanId(planId);
            setIsCreating(false);
            setIsEditing(false);
            resetMessages();
            navigate(`/plans/${planId}`, { replace: true });
          }}
        />
      </Panel>

      <Panel
        title={isCreating ? t('web.plans.page.createTitle') : isEditing ? t('web.plans.page.editTitle', { name: formData?.name ?? '' }) : selectedPlan?.name ?? t('web.plans.details')}
        subtitle={isCreating ? t('web.plans.page.createSubtitle') : isEditing ? t('web.plans.page.planIdSubtitle', { id: formData?.id ?? '' }) : selectedPlan ? t('web.plans.page.planIdSubtitle', { id: selectedPlan.id }) : t('web.plans.page.selectHint')}
        preserveTitleCase={Boolean((isEditing && formData?.name) || (!isCreating && !isEditing && selectedPlan))}
        actions={selectedPlan && !isCreating && !isEditing ? (
          <div className="plan-detail-actions">
            <button type="button" onClick={handleEdit} disabled={!canManage}>{t('web.actions.edit')}</button>
            <button type="button" onClick={handleDelete} className="danger-text" disabled={!canManage || submitting}>{t('web.actions.delete')}</button>
            <button type="button" className="primary" data-testid="execute-selected-plan-button" onClick={() => handleExecutePlan(selectedPlan.id)} disabled={!canExecute || submitting || storeIsBusy || !(plans.find((plan) => plan.id === selectedPlan.id)?.isEnabled ?? selectedPlan.isEnabled)}>{t('web.plans.page.executeNow')}</button>
          </div>
        ) : undefined}>
        {errorMsg ? <MessageBanner tone="error" message={errorMsg} inline /> : null}
        {successMsg ? <MessageBanner tone="success" message={successMsg} inline /> : null}

        {isCreating || isEditing ? (
          formData ? (
            <PlanForm
              formData={formData}
              nodes={nodes}
              submitting={submitting}
              onSubmit={handleSubmit}
              onCancel={handleCancel}
              updateForm={updateForm}
              updateSlave={updateSlave}
              addSlave={addSlave}
              removeSlave={removeSlave}
              parseCommaSeparatedList={parseCommaSeparatedList}
            />
          ) : null
        ) : selectedPlan ? (
          <PlanDetailView selectedPlan={selectedPlan} nodes={nodes} history={history} />
        ) : (
          <div className="empty-state editorial-empty" style={{ margin: 'var(--space-6)' }}>{t('web.plans.page.selectHint')}</div>
        )}
      </Panel>
    </div>
  );
}

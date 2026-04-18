import type { CreateOrUpdatePlanRequest, NodeSummary } from '../../api.ts';
import { useI18n } from '../../i18n/useI18n.ts';
import { FormActions } from '../common/FormActions.tsx';
import { canUseAbsolutePath, formatAbsolutePathValidationError, formatNodeLabel, formatResolvedPathDisplay, getConflictResolutionDescription, getConflictResolutionOptions, getEffectiveMasterNodeId, getNodeById, getPathRuleHint, getSelectableSlaveNodes, getSyncModeOptions, getTriggerTypeOptions, isAbsolutePlanPath } from './planPresentation.ts';

export type PlanFormState = CreateOrUpdatePlanRequest & {
  id: string;
  slaves: (CreateOrUpdatePlanRequest['slaves'][number] & { uiKey: string })[];
};

type PlanFormProps = {
  formData: PlanFormState;
  nodes: NodeSummary[];
  submitting: boolean;
  onSubmit: (event: React.FormEvent<HTMLFormElement>) => void;
  onCancel: () => void;
  updateForm: (updater: (current: PlanFormState) => PlanFormState) => void;
  updateSlave: (index: number, updater: (current: PlanFormState['slaves'][number]) => PlanFormState['slaves'][number]) => void;
  addSlave: () => void;
  removeSlave: (index: number) => void;
  parseCommaSeparatedList: (value: string) => string[];
};

export function PlanForm({
  formData,
  nodes,
  submitting,
  onSubmit,
  onCancel,
  updateForm,
  updateSlave,
  addSlave,
  removeSlave,
  parseCommaSeparatedList,
}: PlanFormProps) {
  const { t } = useI18n();
  const effectiveMasterNodeId = getEffectiveMasterNodeId(formData.masterNodeId);
  const selectedMasterNode = getNodeById(nodes, effectiveMasterNodeId);
  const triggerTypeOptions = getTriggerTypeOptions(t);
  const syncModeOptions = getSyncModeOptions(t);
  const conflictResolutionOptions = getConflictResolutionOptions(t);

  return (
    <form onSubmit={onSubmit} className="plan-form">
      <div className="form-group-grid">
        <label>
          <span>{t('web.plans.form.planId')}</span>
          <input type="text" value={formData.id} disabled readOnly />
        </label>
        <label>
          <span>{t('web.plans.form.planName')}</span>
          <input type="text" value={formData.name} onChange={(event) => updateForm((current) => ({ ...current, name: event.target.value }))} required />
        </label>
        <label className="full-width">
          <span>{t('web.forms.label.description')}</span>
          <input type="text" value={formData.description} onChange={(event) => updateForm((current) => ({ ...current, description: event.target.value }))} />
        </label>

        <label>
          <span>{t('web.plans.sourceNode')}</span>
          <select value={formData.masterNodeId === 'host-local' ? '' : formData.masterNodeId} onChange={(event) => updateForm((current) => ({ ...current, masterNodeId: event.target.value }))}>
            <option value="">{t('web.plans.form.masterNodeDefaultOption')}</option>
            {nodes.filter((node) => !node.isImplicitHostNode).map((node) => (
              <option key={node.id} value={node.id}>{formatNodeLabel(node, node.id, t)}</option>
            ))}
          </select>
        </label>

        <label>
          <span>{t('web.plans.form.syncItemType')}</span>
          <input type="text" value={formData.syncItemType} onChange={(event) => updateForm((current) => ({ ...current, syncItemType: event.target.value }))} placeholder={t('web.plans.form.syncItemTypePlaceholder')} />
        </label>

        <label>
          <span>{t('web.plans.form.triggerType')}</span>
          <select value={formData.triggerType} onChange={(event) => updateForm((current) => ({ ...current, triggerType: event.target.value }))}>
            {triggerTypeOptions.map((option) => (
              <option key={option.value} value={option.value}>{option.label}</option>
            ))}
          </select>
        </label>

        <label>
          <span>{t('web.plans.form.intervalSeconds')}</span>
          <input
            type="number"
            min="1"
            value={formData.intervalSeconds ?? ''}
            onChange={(event) => updateForm((current) => ({ ...current, intervalSeconds: event.target.value ? Number.parseInt(event.target.value, 10) : undefined }))}
            placeholder={t('web.forms.hint.optional')}
          />
        </label>

        <label className="full-width">
          <span>{t('web.plans.form.cronExpression')}</span>
          <input type="text" value={formData.cronExpression ?? ''} onChange={(event) => updateForm((current) => ({ ...current, cronExpression: event.target.value }))} placeholder={t('web.plans.form.cronPlaceholder')} />
        </label>

        <label className="checkbox-label">
          <input type="checkbox" checked={formData.isEnabled} onChange={(event) => updateForm((current) => ({ ...current, isEnabled: event.target.checked }))} />
          <span>{t('web.plans.form.enablePlan')}</span>
        </label>

        <label className="checkbox-label">
          <input type="checkbox" checked={formData.enableFileSystemWatcher} onChange={(event) => updateForm((current) => ({ ...current, enableFileSystemWatcher: event.target.checked }))} />
          <span>{t('web.plans.form.enableWatcher')}</span>
        </label>
      </div>

      <div className="plan-form-section-header">
        <h3>{t('web.plans.form.slaveConfig')}</h3>
        <button type="button" onClick={addSlave}>+ {t('web.plans.form.addSlave')}</button>
      </div>

      {formData.slaves.map((slave, index) => {
        const selectedSlaveNode = nodes.find((node) => node.id === slave.slaveNodeId);
        const showMasterAbsoluteWarning = isAbsolutePlanPath(slave.targetPath) && !canUseAbsolutePath(selectedMasterNode);
        const showSlaveAbsoluteWarning = isAbsolutePlanPath(slave.sourcePath) && !canUseAbsolutePath(selectedSlaveNode);
        return (
          <div key={slave.uiKey} className="slave-edit-card">
            {showMasterAbsoluteWarning ? <div className="message-banner error inline-message">{formatAbsolutePathValidationError('web.plans.masterNodePath', selectedMasterNode, t)}</div> : null}
            {showSlaveAbsoluteWarning ? <div className="message-banner error inline-message">{formatAbsolutePathValidationError('web.plans.slaveNodePath', selectedSlaveNode, t)}</div> : null}

            <div className="slave-edit-header">
              <h4>{t('web.plans.form.slaveIndexTitle', { index: index + 1 })}</h4>
              <button type="button" className="danger-text" onClick={() => removeSlave(index)} disabled={formData.slaves.length === 1}>{t('web.plans.form.removeSlave')}</button>
            </div>

            <div className="form-group-grid">
              <label>
                <span>{t('web.plans.targetNode')}</span>
                <select value={slave.slaveNodeId} onChange={(event) => updateSlave(index, (current) => ({ ...current, slaveNodeId: event.target.value }))}>
                  {getSelectableSlaveNodes(nodes).map((node) => (
                    <option key={node.id} value={node.id}>{formatNodeLabel(node, node.id, t)}</option>
                  ))}
                </select>
              </label>

              <label>
                <span>{t('web.plans.form.syncMode')}</span>
                <select value={slave.syncMode} onChange={(event) => updateSlave(index, (current) => ({ ...current, syncMode: event.target.value }))}>
                  {syncModeOptions.map((option) => (
                    <option key={option.value} value={option.value}>{option.label}</option>
                  ))}
                </select>
              </label>

              <div className="path-row-group full-width">
                <label className="path-label">
                  <span>{t('web.plans.slaveNodePath')}</span>
                  <div className="path-input-wrapper">
                    <span className="path-input-icon">{canUseAbsolutePath(selectedSlaveNode) ? '🔓' : '🔒'}</span>
                    <input type="text" value={slave.sourcePath ?? ''} onChange={(event) => updateSlave(index, (current) => ({ ...current, sourcePath: event.target.value }))} />
                  </div>
                  <small className={`field-hint ${canUseAbsolutePath(selectedSlaveNode) ? 'is-host-local' : ''}`}>
                    {canUseAbsolutePath(selectedSlaveNode) ? <span aria-hidden="true">💡</span> : null}
                    <span>{getPathRuleHint(selectedSlaveNode, t)}</span>
                  </small>
                  <small className="field-path-preview">{t('web.plans.path.finalPathLabel', { path: formatResolvedPathDisplay(selectedSlaveNode, slave.sourcePath, t) })}</small>
                </label>

                <label className="path-label">
                  <span>{t('web.plans.masterNodePath')}</span>
                  <div className="path-input-wrapper">
                    <span className="path-input-icon">{canUseAbsolutePath(selectedMasterNode) ? '🔓' : '🔒'}</span>
                    <input type="text" value={slave.targetPath ?? ''} onChange={(event) => updateSlave(index, (current) => ({ ...current, targetPath: event.target.value }))} />
                  </div>
                  <small className={`field-hint ${canUseAbsolutePath(selectedMasterNode) ? 'is-host-local' : ''}`}>
                    {canUseAbsolutePath(selectedMasterNode) ? <span aria-hidden="true">💡</span> : null}
                    <span>{getPathRuleHint(selectedMasterNode, t)}</span>
                  </small>
                  <small className="field-path-preview">{t('web.plans.path.finalPathLabel', { path: formatResolvedPathDisplay(selectedMasterNode, slave.targetPath, t) })}</small>
                </label>
              </div>

              <label className="full-width">
                <span>{t('web.plans.form.includeFilters')}</span>
                <input
                  type="text"
                  value={(slave.filters ?? []).join(', ')}
                  onChange={(event) => updateSlave(index, (current) => ({ ...current, filters: parseCommaSeparatedList(event.target.value) }))}
                  placeholder={t('web.plans.form.includeFiltersPlaceholder')}
                />
              </label>

              <label className="full-width">
                <span>{t('web.plans.form.excludeFilters')}</span>
                <input
                  type="text"
                  value={(slave.exclusions ?? []).join(', ')}
                  onChange={(event) => updateSlave(index, (current) => ({ ...current, exclusions: parseCommaSeparatedList(event.target.value) }))}
                  placeholder={t('web.plans.form.excludeFiltersPlaceholder')}
                />
              </label>

              <label className="checkbox-label full-width">
                <input type="checkbox" checked={slave.enableDeletionProtection} onChange={(event) => updateSlave(index, (current) => ({ ...current, enableDeletionProtection: event.target.checked }))} />
                <span>{t('web.plans.form.enableDeletionProtection')}</span>
              </label>

              <label>
                <span>{t('web.plans.form.conflictResolution')} <small className="mono">({t('web.plans.form.conflictResolutionSubLabel')})</small></span>
                <div className="conflict-group-wrapper">
                  <select value={slave.conflictResolutionStrategy ?? 'Manual'} onChange={(event) => updateSlave(index, (current) => ({ ...current, conflictResolutionStrategy: event.target.value }))}>
                    {conflictResolutionOptions.map((option) => (
                      <option key={option.value} value={option.value}>{option.label}</option>
                    ))}
                  </select>
                  <span>
                    {getConflictResolutionDescription(slave.conflictResolutionStrategy ?? 'Manual', t)}
                  </span>
                </div>
              </label>
            </div>
          </div>
        );
      })}

      <FormActions
        submitting={submitting}
        submitLabel={t('web.plans.form.savePlan')}
        submittingLabel={t('web.forms.submitting')}
        cancelLabel={t('web.actions.cancel')}
        onCancel={onCancel}
        submitDisabled={formData.slaves.length === 0}
      />
    </form>
  );
}

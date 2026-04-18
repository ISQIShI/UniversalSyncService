import type { CreateOrUpdatePlanRequest, NodeSummary } from '../../api.ts';
import { FormActions } from '../common/FormActions.tsx';
import { canUseAbsolutePath, conflictResolutionDescriptions, conflictResolutionOptions, formatAbsolutePathValidationError, formatNodeLabel, formatResolvedPathDisplay, getEffectiveMasterNodeId, getNodeById, getPathRuleHint, getSelectableSlaveNodes, isAbsolutePlanPath, syncModeOptions, triggerTypeOptions } from './planPresentation.ts';

export type PlanFormState = CreateOrUpdatePlanRequest & {
  id: string;
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
  const effectiveMasterNodeId = getEffectiveMasterNodeId(formData.masterNodeId);
  const selectedMasterNode = getNodeById(nodes, effectiveMasterNodeId);

  return (
    <form onSubmit={onSubmit} className="plan-form">
      <div className="form-group-grid">
        <label>
          <span>计划 ID</span>
          <input type="text" value={formData.id} disabled readOnly />
        </label>
        <label>
          <span>计划名称</span>
          <input type="text" value={formData.name} onChange={(event) => updateForm((current) => ({ ...current, name: event.target.value }))} required />
        </label>
        <label className="full-width">
          <span>描述</span>
          <input type="text" value={formData.description} onChange={(event) => updateForm((current) => ({ ...current, description: event.target.value }))} />
        </label>

        <label>
          <span>主节点</span>
          <select value={formData.masterNodeId === 'host-local' ? '' : formData.masterNodeId} onChange={(event) => updateForm((current) => ({ ...current, masterNodeId: event.target.value }))}>
            <option value="">🖥️ 默认宿主节点 (host-local)</option>
            {nodes.filter((node) => !node.isImplicitHostNode).map((node) => (
              <option key={node.id} value={node.id}>{formatNodeLabel(node, node.id)}</option>
            ))}
          </select>
        </label>

        <label>
          <span>同步对象类型</span>
          <input type="text" value={formData.syncItemType} onChange={(event) => updateForm((current) => ({ ...current, syncItemType: event.target.value }))} placeholder="例如 FileSystem" />
        </label>

        <label>
          <span>触发方式</span>
          <select value={formData.triggerType} onChange={(event) => updateForm((current) => ({ ...current, triggerType: event.target.value }))}>
            {triggerTypeOptions.map((option) => (
              <option key={option.value} value={option.value}>{option.label}</option>
            ))}
          </select>
        </label>

        <label>
          <span>间隔秒数</span>
          <input
            type="number"
            min="1"
            value={formData.intervalSeconds ?? ''}
            onChange={(event) => updateForm((current) => ({ ...current, intervalSeconds: event.target.value ? Number.parseInt(event.target.value, 10) : undefined }))}
            placeholder="可选"
          />
        </label>

        <label className="full-width">
          <span>Cron 表达式</span>
          <input type="text" value={formData.cronExpression ?? ''} onChange={(event) => updateForm((current) => ({ ...current, cronExpression: event.target.value }))} placeholder="定时触发时可选" />
        </label>

        <label className="checkbox-label">
          <input type="checkbox" checked={formData.isEnabled} onChange={(event) => updateForm((current) => ({ ...current, isEnabled: event.target.checked }))} />
          <span>启用计划</span>
        </label>

        <label className="checkbox-label">
          <input type="checkbox" checked={formData.enableFileSystemWatcher} onChange={(event) => updateForm((current) => ({ ...current, enableFileSystemWatcher: event.target.checked }))} />
          <span>启用文件监听</span>
        </label>
      </div>

      <div className="plan-form-section-header">
        <h3>从节点配置</h3>
        <button type="button" onClick={addSlave}>+ 添加从节点</button>
      </div>

      {formData.slaves.map((slave, index) => {
        const selectedSlaveNode = nodes.find((node) => node.id === slave.slaveNodeId);
        const showMasterAbsoluteWarning = isAbsolutePlanPath(slave.targetPath) && !canUseAbsolutePath(selectedMasterNode);
        const showSlaveAbsoluteWarning = isAbsolutePlanPath(slave.sourcePath) && !canUseAbsolutePath(selectedSlaveNode);

        return (
          <div key={`${slave.slaveNodeId}-${index}`} className="slave-edit-card">
            {showMasterAbsoluteWarning ? <div className="message-banner error inline-message">{formatAbsolutePathValidationError('主节点路径', selectedMasterNode)}</div> : null}
            {showSlaveAbsoluteWarning ? <div className="message-banner error inline-message">{formatAbsolutePathValidationError('从节点路径', selectedSlaveNode)}</div> : null}

            <div className="slave-edit-header">
              <h4>从节点 {index + 1}</h4>
              <button type="button" className="danger-text" onClick={() => removeSlave(index)} disabled={formData.slaves.length === 1}>移除</button>
            </div>

            <div className="form-group-grid">
              <label>
                <span>目标节点</span>
                <select value={slave.slaveNodeId} onChange={(event) => updateSlave(index, (current) => ({ ...current, slaveNodeId: event.target.value }))}>
                  {getSelectableSlaveNodes(nodes).map((node) => (
                    <option key={node.id} value={node.id}>{formatNodeLabel(node, node.id)}</option>
                  ))}
                </select>
              </label>

              <label>
                <span>同步模式</span>
                <select value={slave.syncMode} onChange={(event) => updateSlave(index, (current) => ({ ...current, syncMode: event.target.value }))}>
                  {syncModeOptions.map((option) => (
                    <option key={option.value} value={option.value}>{option.label}</option>
                  ))}
                </select>
              </label>

              <div className="path-row-group full-width">
                <label className="path-label">
                  <span>从节点路径</span>
                  <div className="path-input-wrapper">
                    <span className="path-input-icon">{canUseAbsolutePath(selectedSlaveNode) ? '🔓' : '🔒'}</span>
                    <input type="text" value={slave.sourcePath ?? ''} onChange={(event) => updateSlave(index, (current) => ({ ...current, sourcePath: event.target.value }))} />
                  </div>
                  <small className={`field-hint ${canUseAbsolutePath(selectedSlaveNode) ? 'is-host-local' : ''}`}>
                    {canUseAbsolutePath(selectedSlaveNode) ? <span aria-hidden="true">💡</span> : null}
                    <span>{getPathRuleHint(selectedSlaveNode)}</span>
                  </small>
                  <small className="field-path-preview">最终路径：{formatResolvedPathDisplay(selectedSlaveNode, slave.sourcePath)}</small>
                </label>

                <label className="path-label">
                  <span>主节点路径</span>
                  <div className="path-input-wrapper">
                    <span className="path-input-icon">{canUseAbsolutePath(selectedMasterNode) ? '🔓' : '🔒'}</span>
                    <input type="text" value={slave.targetPath ?? ''} onChange={(event) => updateSlave(index, (current) => ({ ...current, targetPath: event.target.value }))} />
                  </div>
                  <small className={`field-hint ${canUseAbsolutePath(selectedMasterNode) ? 'is-host-local' : ''}`}>
                    {canUseAbsolutePath(selectedMasterNode) ? <span aria-hidden="true">💡</span> : null}
                    <span>{getPathRuleHint(selectedMasterNode)}</span>
                  </small>
                  <small className="field-path-preview">最终路径：{formatResolvedPathDisplay(selectedMasterNode, slave.targetPath)}</small>
                </label>
              </div>

              <label className="full-width">
                <span>包含过滤规则</span>
                <input
                  type="text"
                  value={(slave.filters ?? []).join(', ')}
                  onChange={(event) => updateSlave(index, (current) => ({ ...current, filters: parseCommaSeparatedList(event.target.value) }))}
                  placeholder="例如 *.md, assets/**"
                />
              </label>

              <label className="full-width">
                <span>排除规则</span>
                <input
                  type="text"
                  value={(slave.exclusions ?? []).join(', ')}
                  onChange={(event) => updateSlave(index, (current) => ({ ...current, exclusions: parseCommaSeparatedList(event.target.value) }))}
                  placeholder="例如 .git/, *.tmp"
                />
              </label>

              <label className="checkbox-label full-width">
                <input type="checkbox" checked={slave.enableDeletionProtection} onChange={(event) => updateSlave(index, (current) => ({ ...current, enableDeletionProtection: event.target.checked }))} />
                <span>启用删除保护</span>
              </label>

              <label>
                <span>冲突处理 <small className="mono">(双向修改策略)</small></span>
                <div className="conflict-group-wrapper">
                  <select value={slave.conflictResolutionStrategy ?? 'Manual'} onChange={(event) => updateSlave(index, (current) => ({ ...current, conflictResolutionStrategy: event.target.value }))}>
                    {conflictResolutionOptions.map((option) => (
                      <option key={option.value} value={option.value}>{option.label}</option>
                    ))}
                  </select>
                  <span>
                    {conflictResolutionDescriptions[slave.conflictResolutionStrategy ?? 'Manual'] || '自动解决冲突的备选策略。'}
                  </span>
                </div>
              </label>
            </div>
          </div>
        );
      })}

      <FormActions
        submitting={submitting}
        submitLabel="保存计划"
        onCancel={onCancel}
        submitDisabled={formData.slaves.length === 0}
      />
    </form>
  );
}

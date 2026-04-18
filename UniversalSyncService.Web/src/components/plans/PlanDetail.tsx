import type { HistoryEntry, NodeSummary, PlanDetail } from '../../api.ts';
import { StatusBadge } from '../common/StatusBadge.tsx';
import { formatConfiguredAndResolvedPath, formatConflictResolutionStrategyLabel, formatMasterNodeLabel, formatNodeLabel, formatSyncModeLabel, formatTriggerTypeLabel } from './planPresentation.ts';
import { useI18n } from '../../i18n/useI18n.ts';
import { formatDate } from '../../i18n/format.ts';

type PlanDetailProps = {
  selectedPlan: PlanDetail;
  nodes: NodeSummary[];
  history: HistoryEntry[];
};

// 【布局重构】不再局限于多列 detail-grid
// 采用具有粗野感的横向拉通(hero区块)和分明大字体的区域
export function PlanDetailView({ selectedPlan, nodes, history }: PlanDetailProps) {
  const { t, locale } = useI18n();
  const nodeMap = new Map(nodes.map((node) => [node.id, node.name]));
  const masterNode = nodes.find((node) => node.id === selectedPlan.masterNodeId);

  return (
    <div className="plan-detail-stack editorial-container">
      <div className="editorial-hero">
        <div className="hero-stat main-stat">
          <span>{t('web.plans.sourceNode')}</span>
          <strong>{formatMasterNodeLabel(selectedPlan.masterNodeId, nodes, t)}</strong>
        </div>
        <div className="hero-stat">
          <span>{t('web.plans.syncObject')}</span>
          <strong>{selectedPlan.syncItemType}</strong>
        </div>
        <div className="hero-stat">
          <span>{t('web.plans.form.triggerType')}</span>
          <strong>{formatTriggerTypeLabel(selectedPlan.triggerType, t)}</strong>
        </div>
      </div>

      <div className="editorial-meta-strip">
        {/* 【测试稳定性】为累计执行次数保留稳定 data-testid，避免 E2E 依赖纯样式类名 */}
        <div className="meta-item" data-testid="plan-execution-count-card">
          <span>{t('web.plans.executionCount')}</span>
          <strong data-testid="plan-execution-count-value">{selectedPlan.executionCount}</strong>
        </div>
        <div className="meta-item">
          <span>{t('web.plans.form.enableWatcher')}</span>
          <strong>{selectedPlan.enableFileSystemWatcher ? t('web.plans.status.enabled') : t('web.plans.status.disabled')}</strong>
        </div>
        {selectedPlan.intervalSeconds ? (
          <div className="meta-item">
            <span>{t('web.plans.schedule')}</span>
            <strong>{selectedPlan.intervalSeconds}s</strong>
          </div>
        ) : null}
        {selectedPlan.cronExpression ? (
          <div className="meta-item">
            <span>{t('web.plans.form.cronExpression')}</span>
            <strong>{selectedPlan.cronExpression}</strong>
          </div>
        ) : null}
      </div>

      <div className="editorial-section-title">
        <h3>{t('web.plans.form.slaveConfig')}</h3>
        <span className="section-decorator">/{selectedPlan.slaves.length}</span>
      </div>
      
      <div className="editorial-slave-grid">
        {selectedPlan.slaves.map((slave, index) => {
          const slaveNode = nodes.find((node) => node.id === slave.slaveNodeId);
          const slaveCardKey = [
            selectedPlan.id,
            slave.slaveNodeId,
            slave.sourcePath ?? '.',
            slave.targetPath ?? '.',
            index,
          ].join(':');

          return (
            <article key={slaveCardKey} className="editorial-slave-card">
              <div className="slave-card-header">
                <strong>{formatNodeLabel(slaveNode, slave.slaveNodeId, t)}</strong>
                <StatusBadge tone={slave.enableDeletionProtection ? 'warning' : 'muted'} label={slave.enableDeletionProtection ? t('web.plans.deletionProtection') : t('web.plans.standardMode')} />
              </div>
              <div className="slave-card-body">
                <div className="mode-row">
                  <span>{t('web.plans.form.syncMode')}</span>
                  <strong>{formatSyncModeLabel(slave.syncMode, t)}</strong>
                </div>
                <div className="mode-row">
                  <span>{t('web.plans.form.conflictResolution')}</span>
                  <strong>{formatConflictResolutionStrategyLabel(slave.conflictResolutionStrategy, t)}</strong>
                </div>
                <div className="path-row">
                  <span>{t('web.plans.slaveNodePath')}</span>
                  <div className="mono path-value">{formatConfiguredAndResolvedPath(slaveNode, slave.sourcePath, t)}</div>
                </div>
                <div className="path-row">
                  <span>{t('web.plans.masterNodePath')}</span>
                  <div className="mono path-value">{formatConfiguredAndResolvedPath(masterNode, slave.targetPath, t)}</div>
                </div>
                {(slave.filters && slave.filters.length > 0) ? (
                  <div className="path-row">
                    <span>{t('web.plans.form.includeFilters')}</span>
                    <div className="mono path-value">{slave.filters.join(', ')}</div>
                  </div>
                ) : null}
                {(slave.exclusions && slave.exclusions.length > 0) ? (
                  <div className="path-row">
                    <span>{t('web.plans.form.excludeFilters')}</span>
                    <div className="mono path-value">{slave.exclusions.join(', ')}</div>
                  </div>
                ) : null}
              </div>
            </article>
          );
        })}
      </div>

      <div className="editorial-section-title">
        <h3 className="plan-history-title">{t('web.plans.history.title')}</h3>
      </div>
      {history.length > 0 ? (
        <table className="console-table editorial-table" data-testid="history-table">
          <thead>
            <tr>
              <th>{t('web.plans.targetNode')}</th>
              <th>{t('web.plans.history.filePath')}</th>
              <th>{t('web.plans.history.version')}</th>
              <th>{t('web.plans.history.state')}</th>
              <th>{t('web.plans.history.time')}</th>
            </tr>
          </thead>
          <tbody>
            {history.map((entry) => (
              <tr key={entry.id}>
                <td>{nodeMap.get(entry.nodeId) ?? entry.nodeId}</td>
                <td className="mono" title={entry.path}>{entry.path}</td>
                <td>v{entry.syncVersion}</td>
                <td>
                  <StatusBadge tone={entry.state.includes('Success') ? 'healthy' : entry.state.includes('Fail') ? 'danger' : 'muted'} label={entry.state} />
                </td>
                <td className="mono-time">{formatDate(entry.syncTimestamp, locale)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      ) : (
        <div className="empty-state empty-state-inline editorial-empty">{t('web.plans.history.empty')}</div>
      )}
    </div>
  );
}

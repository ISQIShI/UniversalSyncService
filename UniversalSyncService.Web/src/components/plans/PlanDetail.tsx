import type { HistoryEntry, NodeSummary, PlanDetail } from '../../api.ts';
import { StatusBadge } from '../common/StatusBadge.tsx';
import { formatConfiguredAndResolvedPath, formatConflictResolutionStrategyLabel, formatMasterNodeLabel, formatNodeLabel, formatSyncModeLabel, formatTriggerTypeLabel } from './planPresentation.ts';

type PlanDetailProps = {
  selectedPlan: PlanDetail;
  nodes: NodeSummary[];
  history: HistoryEntry[];
};

// 【布局重构】不再局限于多列 detail-grid
// 采用具有粗野感的横向拉通(hero区块)和分明大字体的区域
export function PlanDetailView({ selectedPlan, nodes, history }: PlanDetailProps) {
  const nodeMap = new Map(nodes.map((node) => [node.id, node.name]));
  const masterNode = nodes.find((node) => node.id === selectedPlan.masterNodeId);

  return (
    <div className="plan-detail-stack editorial-container">
      <div className="editorial-hero">
        <div className="hero-stat main-stat">
          <span>主节点</span>
          <strong>{formatMasterNodeLabel(selectedPlan.masterNodeId, nodes)}</strong>
        </div>
        <div className="hero-stat">
          <span>同步对象</span>
          <strong>{selectedPlan.syncItemType}</strong>
        </div>
        <div className="hero-stat">
          <span>触发方式</span>
          <strong>{formatTriggerTypeLabel(selectedPlan.triggerType)}</strong>
        </div>
      </div>

      <div className="editorial-meta-strip">
        {/* 【测试稳定性】为累计执行次数保留稳定 data-testid，避免 E2E 依赖纯样式类名 */}
        <div className="meta-item" data-testid="plan-execution-count-card">
          <span>累计执行次数</span>
          <strong data-testid="plan-execution-count-value">{selectedPlan.executionCount}</strong>
        </div>
        <div className="meta-item">
          <span>文件监听</span>
          <strong>{selectedPlan.enableFileSystemWatcher ? '已启用' : '未启用'}</strong>
        </div>
        {selectedPlan.intervalSeconds ? (
          <div className="meta-item">
            <span>调度间隔</span>
            <strong>{selectedPlan.intervalSeconds}s</strong>
          </div>
        ) : null}
        {selectedPlan.cronExpression ? (
          <div className="meta-item">
            <span>Cron 表达式</span>
            <strong>{selectedPlan.cronExpression}</strong>
          </div>
        ) : null}
      </div>

      <div className="editorial-section-title">
        <h3>从节点配置</h3>
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
                <strong>{formatNodeLabel(slaveNode, slave.slaveNodeId)}</strong>
                <StatusBadge tone={slave.enableDeletionProtection ? 'warning' : 'muted'} label={slave.enableDeletionProtection ? '删除保护' : '标准模式'} />
              </div>
              <div className="slave-card-body">
                <div className="mode-row">
                  <span>同步模式</span>
                  <strong>{formatSyncModeLabel(slave.syncMode)}</strong>
                </div>
                <div className="mode-row">
                  <span>冲突处理</span>
                  <strong>{formatConflictResolutionStrategyLabel(slave.conflictResolutionStrategy)}</strong>
                </div>
                <div className="path-row">
                  <span>从节点路径</span>
                  <div className="mono path-value">{formatConfiguredAndResolvedPath(slaveNode, slave.sourcePath)}</div>
                </div>
                <div className="path-row">
                  <span>主节点路径</span>
                  <div className="mono path-value">{formatConfiguredAndResolvedPath(masterNode, slave.targetPath)}</div>
                </div>
                {(slave.filters && slave.filters.length > 0) ? (
                  <div className="path-row">
                    <span>包含</span>
                    <div className="mono path-value">{slave.filters.join(', ')}</div>
                  </div>
                ) : null}
                {(slave.exclusions && slave.exclusions.length > 0) ? (
                  <div className="path-row">
                    <span>排除</span>
                    <div className="mono path-value">{slave.exclusions.join(', ')}</div>
                  </div>
                ) : null}
              </div>
            </article>
          );
        })}
      </div>

      <div className="editorial-section-title">
        <h3 className="plan-history-title">执行历史</h3>
      </div>
      {history.length > 0 ? (
        <table className="console-table editorial-table" data-testid="history-table">
          <thead>
            <tr>
              <th>目标节点</th>
              <th>文件路径</th>
              <th>版本</th>
              <th>状态</th>
              <th>时间</th>
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
                <td className="mono-time">{new Date(entry.syncTimestamp).toLocaleString()}</td>
              </tr>
            ))}
          </tbody>
        </table>
      ) : (
        <div className="empty-state empty-state-inline editorial-empty">该计划暂无执行历史。</div>
      )}
    </div>
  );
}

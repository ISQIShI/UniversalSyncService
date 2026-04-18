import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { type ExecutePlanResponse } from '../api.ts';
import { useAppStore } from '../store/useAppStore.ts';
import { Panel } from '../components/common/Panel.tsx';
import { StatusBadge } from '../components/common/StatusBadge.tsx';

export function DashboardPage() {
  const navigate = useNavigate();
  const { status, plans, globalHistory, isBusy, isConnected, canUseAnonymousApi, executePlanNow } = useAppStore();
  const canExecute = canUseAnonymousApi || isConnected;
  
  const [executeResult, setExecuteResult] = useState<ExecutePlanResponse | null>(null);

  const handleExecute = async (planId: string) => {
    if (!canExecute) return;
    try {
      const result = await executePlanNow(planId);
      setExecuteResult(result);
    } catch {
      // 全局错误消息已由 store 统一处理。
    }
  };

  return (
    <div className="page-grid">
      {/* 【UI/UX】将总览区域独立为一个大跨度的 Header 区块，增强视觉冲击力 */}
      <header className="dashboard-header" style={{ marginBottom: 'var(--space-6)' }}>
        <h2 style={{ fontFamily: 'var(--font-display)', fontSize: '3rem', letterSpacing: '-0.04em', borderBottom: '4px solid var(--border-strong)', paddingBottom: '1rem', marginBottom: '2rem' }}>
          <span>全局状态 {'//'} </span>
          <span className="preserve-case">{status?.serviceName ?? '未知服务'}</span>
        </h2>
        <div className="stat-grid" style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(280px, 1fr))', gap: 'var(--space-4)' }}>
          <article className="stat-tile">
            <span>活跃任务</span>
            <strong>{status?.activeTaskCount ?? 0}</strong>
          </article>
          <article className="stat-tile">
            <span>同步计划总数</span>
            <strong>{status?.planCount ?? 0}</strong>
          </article>
          <article className="stat-tile">
            <span>稳定运行 (分钟)</span>
            <strong>{status ? Math.floor(status.uptimeSeconds / 60) : '-'}</strong>
          </article>
        </div>
      </header>

      {executeResult && (
        <div style={{ marginBottom: 'var(--space-6)' }}>
          <Panel title="最后一次执行指令" subtitle={`目标：${executeResult.planId}`}>
            <div className="result-strip" style={{ display: 'flex', gap: 'var(--space-6)', padding: 'var(--space-4)', fontSize: '1.25rem', fontFamily: 'var(--font-display)', fontWeight: 700 }}>
              <span style={{ color: 'var(--text-main)' }}>总数: {executeResult.totalTasks}</span>
              <span style={{ color: 'var(--status-good)' }}>成功: {executeResult.successCount}</span>
              <span style={{ color: 'var(--text-muted)' }}>忽略: {executeResult.noChangesCount}</span>
              <span style={{ color: 'var(--status-warn)' }}>冲突: {executeResult.conflictCount}</span>
              <span style={{ color: 'var(--status-bad)' }}>失败: {executeResult.failedCount}</span>
            </div>
          </Panel>
        </div>
      )}

      {/* 【UI/UX】采用非对称的两列布局 (2:1)，强化主要操作区域的优先级 */}
      <div className="dashboard-grid" style={{ gridTemplateColumns: '1.5fr 1fr', gridTemplateAreas: '"plans history"' }}>
        <div style={{ gridArea: 'plans' }}>
          {/* 【注意】保留 dashboard-plan-table 作为 e2e 测试定位依赖 */}
          <Panel
            title="可用计划拓扑"
            subtitle="管理与分发指令"
            testId="dashboard-plan-table">
            {plans.length > 0 ? (
              <div className="plan-quick-list" style={{ border: 'none', borderTop: '2px solid var(--border-strong)' }}>
                {plans.map((plan) => (
                  <div key={plan.id} className="plan-row" style={{ borderBottom: '2px solid var(--border-strong)', padding: 'var(--space-5) var(--space-4)', gap: 'var(--space-6)' }}>
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '8px', flex: 1 }}>
                      <div className="entity-title-row">
                        <StatusBadge tone={plan.isEnabled ? 'healthy' : 'muted'} label={plan.isEnabled ? 'LIVE' : 'HALTED'} />
                        <h4 className="preserve-case" style={{ margin: 0, fontFamily: 'var(--font-display)', fontSize: '1.5rem', lineHeight: 1 }}>{plan.name}</h4>
                      </div>
                      <div style={{ display: 'flex', gap: 'var(--space-4)', color: 'var(--text-muted)', fontFamily: 'var(--font-mono)', fontSize: '0.85rem' }}>
                        <span>ID: {plan.id}</span>
                        <span>|</span>
                        <span>引擎: {plan.syncItemType}</span>
                      </div>
                    </div>
                    
                    <div className="plan-row-actions" style={{ display: 'flex', flexDirection: 'column', gap: 'var(--space-3)', alignItems: 'flex-end' }}>
                      {/* 【注意】保留 execute-plan- 前缀及 plan-link- 前缀 */}
                      <button
                        type="button"
                        className="primary"
                        style={{ fontSize: '1rem', padding: '0.75rem 2rem' }}
                        data-testid={`execute-plan-${plan.id}`}
                        onClick={() => handleExecute(plan.id)}
                        disabled={!canExecute || isBusy || !plan.isEnabled}>
                        执行 (F5)
                      </button>
                      <button
                        type="button"
                        data-testid={`plan-link-${plan.id}`}
                        onClick={() => navigate(`/plans/${plan.id}`)}
                        style={{ fontSize: '0.75rem', padding: '0.25rem 0.5rem', background: 'transparent', boxShadow: 'none', border: 'none', textDecoration: 'underline', color: 'var(--text-muted)' }}>
                        配置面板 →
                      </button>
                    </div>
                  </div>
                ))}
              </div>
            ) : (
              <div className="empty-state">当前没有同步计划配置。</div>
            )}
          </Panel>
        </div>

        <div style={{ gridArea: 'history' }}>
          <Panel title="系统日志账本" subtitle="全局历史记录轨迹" testId="history-table">
            {globalHistory.length > 0 ? (
              <div style={{ display: 'flex', flexDirection: 'column', gap: '0' }}>
                {globalHistory.map((entry) => (
                  <div key={entry.id} style={{ display: 'flex', flexDirection: 'column', padding: 'var(--space-4)', borderBottom: '1px solid var(--border-subtle)', background: 'var(--bg-base)' }}>
                    <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '8px' }}>
                      <strong style={{ fontFamily: 'var(--font-display)', fontSize: '1rem' }}>{entry.planId}</strong>
                      <StatusBadge 
                         tone={entry.state.includes('Success') ? 'healthy' : entry.state.includes('Fail') ? 'danger' : 'warning'} 
                         label={entry.state} 
                       />
                    </div>
                    <span className="mono" style={{ fontSize: '0.75rem', color: 'var(--text-dim)', marginBottom: '8px', wordBreak: 'break-all' }}>{entry.path}</span>
                    <span className="mono" style={{ fontSize: '0.7rem', color: 'var(--text-muted)', textAlign: 'right' }}>
                      {new Date(entry.syncTimestamp).toLocaleString()}
                    </span>
                  </div>
                ))}
              </div>
            ) : (
              <div className="empty-state">暂无全局历史记录。</div>
            )}
          </Panel>
        </div>
      </div>
    </div>
  );
}



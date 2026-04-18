import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { type ExecutePlanResponse } from '../api.ts';
import { useAppStore } from '../store/useAppStore.ts';
import { Panel } from '../components/common/Panel.tsx';
import { StatusBadge } from '../components/common/StatusBadge.tsx';
import { useI18n } from '../i18n/useI18n.ts';
import { formatDate } from '../i18n/format.ts';

export function DashboardPage() {
  const navigate = useNavigate();
  const { status, plans, globalHistory, isBusy, isConnected, canUseAnonymousApi, executePlanNow } = useAppStore();
  const canExecute = canUseAnonymousApi || isConnected;
  const { t, locale } = useI18n();
  
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
          <span>{t('web.dashboard.globalStatus')}</span>
          <span className="preserve-case">{status?.serviceName ?? t('web.dashboard.unknownService')}</span>
        </h2>
        <div className="stat-grid" style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(280px, 1fr))', gap: 'var(--space-4)' }}>
          <article className="stat-tile">
            <span>{t('web.dashboard.activeTasks')}</span>
            <strong>{status?.activeTaskCount ?? 0}</strong>
          </article>
          <article className="stat-tile">
            <span>{t('web.dashboard.totalPlans')}</span>
            <strong>{status?.planCount ?? 0}</strong>
          </article>
          <article className="stat-tile">
            <span>{t('web.dashboard.uptime')}</span>
            <strong>{status ? Math.floor(status.uptimeSeconds / 60) : '-'}</strong>
          </article>
        </div>
      </header>

      {executeResult && (
        <div style={{ marginBottom: 'var(--space-6)' }}>
          <Panel title={t('web.dashboard.lastExecution')} subtitle={t('web.dashboard.target', { planId: executeResult.planId })}>
            <div className="result-strip" style={{ display: 'flex', gap: 'var(--space-6)', padding: 'var(--space-4)', fontSize: '1.25rem', fontFamily: 'var(--font-display)', fontWeight: 700 }}>
              <span style={{ color: 'var(--text-main)' }}>{t('web.dashboard.exec.total')}: {executeResult.totalTasks}</span>
              <span style={{ color: 'var(--status-good)' }}>{t('web.dashboard.exec.success')}: {executeResult.successCount}</span>
              <span style={{ color: 'var(--text-muted)' }}>{t('web.dashboard.exec.ignored')}: {executeResult.noChangesCount}</span>
              <span style={{ color: 'var(--status-warn)' }}>{t('web.dashboard.exec.conflict')}: {executeResult.conflictCount}</span>
              <span style={{ color: 'var(--status-bad)' }}>{t('web.dashboard.exec.failed')}: {executeResult.failedCount}</span>
            </div>
          </Panel>
        </div>
      )}

      {/* 【UI/UX】采用非对称的两列布局 (2:1)，强化主要操作区域的优先级 */}
      <div className="dashboard-grid" style={{ gridTemplateColumns: '1.5fr 1fr', gridTemplateAreas: '"plans history"' }}>
        <div style={{ gridArea: 'plans' }}>
          {/* 【注意】保留 dashboard-plan-table 作为 e2e 测试定位依赖 */}
          <Panel
            title={t('web.dashboard.plans.title')}
            subtitle={t('web.dashboard.plans.subtitle')}
            testId="dashboard-plan-table">
            {plans.length > 0 ? (
              <div className="plan-quick-list" style={{ border: 'none', borderTop: '2px solid var(--border-strong)' }}>
                {plans.map((plan) => (
                  <div key={plan.id} className="plan-row" style={{ borderBottom: '2px solid var(--border-strong)', padding: 'var(--space-5) var(--space-4)', gap: 'var(--space-6)' }}>
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '8px', flex: 1 }}>
                      <div className="entity-title-row">
                        <StatusBadge tone={plan.isEnabled ? 'healthy' : 'muted'} label={plan.isEnabled ? t('web.status.live') : t('web.status.halted')} />
                        <h4 className="preserve-case" style={{ margin: 0, fontFamily: 'var(--font-display)', fontSize: '1.5rem', lineHeight: 1 }}>{plan.name}</h4>
                      </div>
                      <div style={{ display: 'flex', gap: 'var(--space-4)', color: 'var(--text-muted)', fontFamily: 'var(--font-mono)', fontSize: '0.85rem' }}>
                        <span>{t('web.dashboard.plans.id', { id: plan.id })}</span>
                        <span>|</span>
                        <span>{t('web.dashboard.plans.engine', { engine: plan.syncItemType })}</span>
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
                        {t('web.dashboard.plans.execute')}
                      </button>
                      <button
                        type="button"
                        data-testid={`plan-link-${plan.id}`}
                        onClick={() => navigate(`/plans/${plan.id}`)}
                        style={{ fontSize: '0.75rem', padding: '0.25rem 0.5rem', background: 'transparent', boxShadow: 'none', border: 'none', textDecoration: 'underline', color: 'var(--text-muted)' }}>
                        {t('web.dashboard.plans.config')}
                      </button>
                    </div>
                  </div>
                ))}
              </div>
            ) : (
              <div className="empty-state">{t('web.dashboard.plans.empty')}</div>
            )}
          </Panel>
        </div>

        <div style={{ gridArea: 'history' }}>
          <Panel title={t('web.dashboard.history.title')} subtitle={t('web.dashboard.history.subtitle')} testId="history-table">
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
                      {formatDate(entry.syncTimestamp, locale)}
                    </span>
                  </div>
                ))}
              </div>
            ) : (
              <div className="empty-state">{t('web.dashboard.history.empty')}</div>
            )}
          </Panel>
        </div>
      </div>
    </div>
  );
}



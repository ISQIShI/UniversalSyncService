import type { NodeDetail as NodeDetailType } from '../../api.ts';
import { useI18n } from '../../i18n/useI18n.ts';
import { formatDate } from '../../i18n/format.ts';

type NodeDetailProps = {
  selectedNode: NodeDetailType;
};

// 【布局重构】不再使用均等小卡片的 detail-grid，改为更具粗野主义风格的大字排版结构
// 分为概览层 (editorial-hero) 和详情列表 (config-list) 两个块区
export function NodeDetailView({ selectedNode }: NodeDetailProps) {
  const { t, locale } = useI18n();
  return (
    <div className="plan-detail-stack editorial-container">
      <div className="editorial-hero">
        <div className="hero-stat">
          <span>{t('web.nodes.type')}</span>
          <strong>{selectedNode.nodeType}</strong>
        </div>
        <div className="hero-stat">
          <span>{t('web.nodes.detail.source')}</span>
          <strong>{selectedNode.sourceLabel}</strong>
        </div>
        <div className="hero-stat">
          <span>{t('web.nodes.detail.status')}</span>
          <strong>{selectedNode.isEnabled ? t('web.plans.status.enabled') : t('web.plans.status.disabled')}</strong>
        </div>
      </div>

      <div className="editorial-path-block">
        <span>{t('web.nodes.form.rootPathLabel.default')}</span>
        <strong className="mono">{selectedNode.rootPath ?? '-'}</strong>
      </div>

      <div className="config-list editorial-config">
        <div className="config-item config-header"><span>{t('web.nodes.form.connectionSettings')}</span><strong>{t('web.nodes.detail.itemCount', { count: Object.keys(selectedNode.connectionSettings).length })}</strong></div>
        {Object.entries(selectedNode.connectionSettings).map(([key, value]) => (
          <div key={key} className="config-item"><span>{key}</span><strong>{value}</strong></div>
        ))}
        <div className="config-item"><span>{t('web.nodes.detail.createdAt')}</span><strong>{formatDate(selectedNode.createdAt, locale)}</strong></div>
        <div className="config-item"><span>{t('web.nodes.detail.modifiedAt')}</span><strong>{formatDate(selectedNode.modifiedAt, locale)}</strong></div>
      </div>
    </div>
  );
}

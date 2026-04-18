import type { ConfigSummary } from '../../api.ts';
import { MessageBanner } from '../common/MessageBanner.tsx';
import { StatusBadge } from '../common/StatusBadge.tsx';
import { useI18n } from '../../i18n/useI18n.ts';

type SystemConfigPanelProps = {
  configSummary: ConfigSummary | null;
};

export function SystemConfigPanel({ configSummary }: SystemConfigPanelProps) {
  const { t } = useI18n();

  if (!configSummary) {
    return <div className="empty-state">{t('web.system.config.empty')}</div>;
  }

  return (
    <>
      <MessageBanner
        tone="info"
        message={t('web.system.config.notice')}
        inline
      />
      <div className="config-list">
        <div className="config-item">
          <span>{t('web.system.config.serviceName')}</span>
          <strong>{configSummary.serviceName}</strong>
        </div>
        <div className="config-item">
          <span>{t('web.system.config.configPath')}</span>
          <strong>{configSummary.configurationFilePath}</strong>
        </div>
        <div className="config-item">
          <span>{t('web.system.config.syncEngine')}</span>
          <StatusBadge tone={configSummary.enableSyncFramework ? 'healthy' : 'muted'} label={configSummary.enableSyncFramework ? t('web.system.config.enabled') : t('web.system.config.disabled')} />
        </div>
        <div className="config-item">
          <span>{t('web.system.config.historyDb')}</span>
          <strong>{configSummary.historyStorePath}</strong>
        </div>
        <div className="config-item">
          <span>{t('web.system.config.resourceScale')}</span>
          <strong>{t('web.system.config.resourceStats', { nodeCount: configSummary.nodeCount, planCount: configSummary.planCount })}</strong>
        </div>
        <div className="config-item">
          <span>{t('web.system.config.pluginSystem')}</span>
          <StatusBadge tone={configSummary.enablePluginSystem ? 'healthy' : 'muted'} label={configSummary.enablePluginSystem ? t('web.system.config.enabled') : t('web.system.config.disabled')} />
        </div>
        <div className="config-item">
          <span>{t('web.system.config.pluginDir')}</span>
          <strong>{configSummary.pluginDirectory}</strong>
        </div>
      </div>
    </>
  );
}

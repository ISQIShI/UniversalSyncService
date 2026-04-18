import { Panel } from '../components/common/Panel.tsx';
import { PluginsTable } from '../components/system/PluginsTable.tsx';
import { SystemConfigPanel } from '../components/system/SystemConfigPanel.tsx';
import { useAppStore } from '../store/useAppStore.ts';
import { useI18n } from '../i18n/useI18n.ts';

export function SystemPage() {
  const { configSummary, plugins } = useAppStore();
  const { t } = useI18n();

  return (
    <div className="page-grid system-grid">
      <Panel title={t('web.system.config.title')} subtitle={t('web.system.config.subtitle')}>
        <SystemConfigPanel configSummary={configSummary} />
      </Panel>

      <Panel title={t('web.system.plugins.title')} subtitle={t('web.system.plugins.subtitle')}>
        <PluginsTable plugins={plugins} />
      </Panel>
    </div>
  );
}

import { Panel } from '../components/common/Panel.tsx';
import { PluginsTable } from '../components/system/PluginsTable.tsx';
import { SystemConfigPanel } from '../components/system/SystemConfigPanel.tsx';
import { useAppStore } from '../store/useAppStore.ts';

export function SystemPage() {
  const { configSummary, plugins } = useAppStore();

  return (
    <div className="page-grid system-grid">
      <Panel title="系统配置" subtitle="运行参数与配置路径">
        <SystemConfigPanel configSummary={configSummary} />
      </Panel>

      <Panel title="已加载插件" subtitle="当前运行时中的扩展能力">
        <PluginsTable plugins={plugins} />
      </Panel>
    </div>
  );
}

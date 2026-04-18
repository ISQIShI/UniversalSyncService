import type { ConfigSummary } from '../../api.ts';
import { MessageBanner } from '../common/MessageBanner.tsx';
import { StatusBadge } from '../common/StatusBadge.tsx';

type SystemConfigPanelProps = {
  configSummary: ConfigSummary | null;
};

export function SystemConfigPanel({ configSummary }: SystemConfigPanelProps) {
  if (!configSummary) {
    return <div className="empty-state">暂时无法读取系统配置。</div>;
  }

  return (
    <>
      <MessageBanner
        tone="info"
        message="提示：宿主默认主节点（host-local）与 Local 类型节点都支持在计划里显式填写绝对路径；其他节点类型仍按各自根路径下的相对路径处理。"
        inline
      />
      <div className="config-list">
        <div className="config-item">
          <span>服务标识</span>
          <strong>{configSummary.serviceName}</strong>
        </div>
        <div className="config-item">
          <span>配置文件路径</span>
          <strong>{configSummary.configurationFilePath}</strong>
        </div>
        <div className="config-item">
          <span>同步引擎</span>
          <StatusBadge tone={configSummary.enableSyncFramework ? 'healthy' : 'muted'} label={configSummary.enableSyncFramework ? '已启用' : '已停用'} />
        </div>
        <div className="config-item">
          <span>历史数据库</span>
          <strong>{configSummary.historyStorePath}</strong>
        </div>
        <div className="config-item">
          <span>当前资源规模</span>
          <strong>{configSummary.nodeCount} 个节点，{configSummary.planCount} 个计划</strong>
        </div>
        <div className="config-item">
          <span>插件系统</span>
          <StatusBadge tone={configSummary.enablePluginSystem ? 'healthy' : 'muted'} label={configSummary.enablePluginSystem ? '已启用' : '已停用'} />
        </div>
        <div className="config-item">
          <span>插件目录</span>
          <strong>{configSummary.pluginDirectory}</strong>
        </div>
      </div>
    </>
  );
}

import type { NodeDetail as NodeDetailType } from '../../api.ts';

type NodeDetailProps = {
  selectedNode: NodeDetailType;
};

// 【布局重构】不再使用均等小卡片的 detail-grid，改为更具粗野主义风格的大字排版结构
// 分为概览层 (editorial-hero) 和详情列表 (config-list) 两个块区
export function NodeDetailView({ selectedNode }: NodeDetailProps) {
  return (
    <div className="plan-detail-stack editorial-container">
      <div className="editorial-hero">
        <div className="hero-stat">
          <span>节点类型</span>
          <strong>{selectedNode.nodeType}</strong>
        </div>
        <div className="hero-stat">
          <span>来源</span>
          <strong>{selectedNode.sourceLabel}</strong>
        </div>
        <div className="hero-stat">
          <span>状态</span>
          <strong>{selectedNode.isEnabled ? '已启用' : '已停用'}</strong>
        </div>
      </div>

      <div className="editorial-path-block">
        <span>根路径</span>
        <strong className="mono">{selectedNode.rootPath ?? '-'}</strong>
      </div>

      <div className="config-list editorial-config">
        <div className="config-item config-header"><span>连接设置</span><strong>{Object.keys(selectedNode.connectionSettings).length} 项</strong></div>
        {Object.entries(selectedNode.connectionSettings).map(([key, value]) => (
          <div key={key} className="config-item"><span>{key}</span><strong>{value}</strong></div>
        ))}
        <div className="config-item"><span>创建时间</span><strong>{new Date(selectedNode.createdAt).toLocaleString()}</strong></div>
        <div className="config-item"><span>修改时间</span><strong>{new Date(selectedNode.modifiedAt).toLocaleString()}</strong></div>
      </div>
    </div>
  );
}

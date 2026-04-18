import type { NodeSummary } from '../../api.ts';
import { StatusBadge } from '../common/StatusBadge.tsx';

type NodeListProps = {
  nodes: NodeSummary[];
  selectedNodeId: string | null;
  isCreating: boolean;
  onSelectNode: (nodeId: string) => void;
};

export function NodeList({ nodes, selectedNodeId, isCreating, onSelectNode }: NodeListProps) {
  if (nodes.length === 0) {
    return <div className="empty-state">暂无节点配置。</div>;
  }

  return (
    <div className="plan-list-pane">
      {nodes.map((node) => {
        const displayPath = node.rootPath ?? node.sourceLabel;

        return (
        <button
          key={node.id}
          type="button"
          className={node.id === selectedNodeId && !isCreating ? 'plan-list-item active' : 'plan-list-item'}
          onClick={() => onSelectNode(node.id)}>
          <div>
            <h4 className="entity-title-row preserve-case">
              <span>{node.isImplicitHostNode ? '🖥️' : '📦'}</span>
              <span>{node.name}</span>
            </h4>
            <p title={displayPath}>{displayPath}</p>
          </div>
          <StatusBadge tone={node.isImplicitHostNode ? 'warning' : node.isEnabled ? 'healthy' : 'muted'} label={node.isImplicitHostNode ? 'host-local' : node.isEnabled ? '已启用' : '已停用'} />
        </button>
      );})}
    </div>
  );
}

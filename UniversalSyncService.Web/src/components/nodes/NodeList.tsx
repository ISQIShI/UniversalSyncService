import type { NodeSummary } from '../../api.ts';
import { StatusBadge } from '../common/StatusBadge.tsx';
import { NodePresentationHelpers } from './nodePresentation.ts';
import { useI18n } from '../../i18n/useI18n.ts';

type NodeListProps = {
  nodes: NodeSummary[];
  selectedNodeId: string | null;
  isCreating: boolean;
  onSelectNode: (nodeId: string) => void;
};

export function NodeList({ nodes, selectedNodeId, isCreating, onSelectNode }: NodeListProps) {
  const { t } = useI18n();

  if (nodes.length === 0) {
    return <div className="empty-state">{NodePresentationHelpers.getEmptyNodeListMessage()}</div>;
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
          <StatusBadge tone={node.isImplicitHostNode ? 'warning' : node.isEnabled ? 'healthy' : 'muted'} label={node.isImplicitHostNode ? 'host-local' : node.isEnabled ? t('web.plans.status.enabled') : t('web.plans.status.disabled')} />
        </button>
      );})}
    </div>
  );
}

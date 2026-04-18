import type { PlanSummary } from '../../api.ts';
import { StatusBadge } from '../common/StatusBadge.tsx';

type PlanListProps = {
  plans: PlanSummary[];
  selectedPlanId: string | null;
  isCreating: boolean;
  onSelectPlan: (planId: string) => void;
};

export function PlanList({ plans, selectedPlanId, isCreating, onSelectPlan }: PlanListProps) {
  if (plans.length === 0) {
    return <div className="empty-state">当前没有同步计划。</div>;
  }

  return (
    <div className="plan-list-pane">
      {plans.map((plan) => (
        <button
          key={plan.id}
          type="button"
          className={plan.id === selectedPlanId && !isCreating ? 'plan-list-item active' : 'plan-list-item'}
          onClick={() => onSelectPlan(plan.id)}>
          <div>
            <h4 className="preserve-case">{plan.name}</h4>
            <p>{plan.description || '暂无描述'}</p>
          </div>
          <StatusBadge tone={plan.isEnabled ? 'healthy' : 'muted'} label={plan.isEnabled ? '已启用' : '已停用'} />
        </button>
      ))}
    </div>
  );
}

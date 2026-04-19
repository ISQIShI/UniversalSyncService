import type { PlanSummary } from '../../api.ts';
import { StatusBadge } from '../common/StatusBadge.tsx';
import { PlanPresentationHelpers } from './planPresentation.ts';
import { useI18n } from '../../i18n/useI18n.ts';

type PlanListProps = {
  plans: PlanSummary[];
  selectedPlanId: string | null;
  isCreating: boolean;
  onSelectPlan: (planId: string) => void;
};

export function PlanList({ plans, selectedPlanId, isCreating, onSelectPlan }: PlanListProps) {
  const { t } = useI18n();

  if (plans.length === 0) {
    return <div className="empty-state">{PlanPresentationHelpers.getEmptyPlanListMessage()}</div>;
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
            <p>{plan.description || PlanPresentationHelpers.getNoDescriptionMessage()}</p>
          </div>
          <StatusBadge tone={plan.isEnabled ? 'healthy' : 'muted'} label={plan.isEnabled ? t('web.plans.status.enabled') : t('web.plans.status.disabled')} />
        </button>
      ))}
    </div>
  );
}

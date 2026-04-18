type StatusBadgeProps = {
  tone: 'healthy' | 'active' | 'warning' | 'danger' | 'muted';
  label: string;
  testId?: string;
};

export function StatusBadge({ tone, label, testId }: StatusBadgeProps) {
  return <span className={`status-badge ${tone}`} data-testid={testId}>{label}</span>;
}




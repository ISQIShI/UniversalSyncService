import type { ReactNode } from 'react';

type PanelProps = {
  title: string;
  subtitle?: string;
  children: ReactNode;
  actions?: ReactNode;
  testId?: string;
  preserveTitleCase?: boolean;
};

export function Panel({ title, subtitle, children, actions, testId, preserveTitleCase }: PanelProps) {
  return (
    <section className="panel" data-testid={testId}>
      <header className="panel-header">
        <div>
          <h2 className={preserveTitleCase ? 'preserve-case' : undefined}>{title}</h2>
          {subtitle ? <p>{subtitle}</p> : null}
        </div>
        {actions ? <div className="panel-actions">{actions}</div> : null}
      </header>
      {children}
    </section>
  );
}




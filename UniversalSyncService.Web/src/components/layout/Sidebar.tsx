import { LayoutDashboard, Network, Settings, Waypoints } from 'lucide-react';
import { useI18n } from '../../i18n/useI18n.ts';

type NavigationItem = 'dashboard' | 'plans' | 'nodes' | 'system';

type SidebarProps = {
  activeItem: NavigationItem;
  onSelect: (item: NavigationItem) => void;
};

export function Sidebar({ activeItem, onSelect }: SidebarProps) {
  const { t } = useI18n();

  const items: Array<{ id: NavigationItem; label: string; hint: string; icon: React.ReactNode }> = [
    { id: 'dashboard', label: t('web.nav.dashboard'), hint: t('web.nav.dashboard.hint'), icon: <LayoutDashboard size={18} /> },
    { id: 'plans', label: t('web.nav.plans'), hint: t('web.nav.plans.hint'), icon: <Waypoints size={18} /> },
    { id: 'nodes', label: t('web.nav.nodes'), hint: t('web.nav.nodes.hint'), icon: <Network size={18} /> },
    { id: 'system', label: t('web.nav.system'), hint: t('web.nav.system.hint'), icon: <Settings size={18} /> },
  ];

  return (
    <aside className="sidebar">
      <div className="sidebar-brand">
        <span className="sidebar-eyebrow">{t('web.app.console')}</span>
        <strong>Universal Sync</strong>
      </div>

      <nav className="sidebar-nav">
        {items.map((item) => (
          <button
            key={item.id}
            type="button"
            className={item.id === activeItem ? 'sidebar-link active' : 'sidebar-link'}
            onClick={() => onSelect(item.id)}>
            <div style={{ display: 'flex', alignItems: 'center', gap: '0.75rem', marginBottom: '4px' }}>
              <span style={{ color: item.id === activeItem ? 'var(--text-main)' : 'var(--text-muted)' }}>
                {item.icon}
              </span>
              <span>{item.label}</span>
            </div>
            <small style={{ paddingLeft: '2.1rem' }}>{item.hint}</small>
          </button>
        ))}
      </nav>
    </aside>
  );
}

export type { NavigationItem };




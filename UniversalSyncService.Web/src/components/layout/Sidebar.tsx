import { LayoutDashboard, Network, Settings, Waypoints } from 'lucide-react';

type NavigationItem = 'dashboard' | 'plans' | 'nodes' | 'system';

type SidebarProps = {
  activeItem: NavigationItem;
  onSelect: (item: NavigationItem) => void;
};

const items: Array<{ id: NavigationItem; label: string; hint: string; icon: React.ReactNode }> = [
  { id: 'dashboard', label: '总览', hint: '概览与历史', icon: <LayoutDashboard size={18} /> },
  { id: 'plans', label: '计划', hint: '同步配置', icon: <Waypoints size={18} /> },
  { id: 'nodes', label: '节点', hint: '基础设施', icon: <Network size={18} /> },
  { id: 'system', label: '系统', hint: '设置与插件', icon: <Settings size={18} /> },
];

export function Sidebar({ activeItem, onSelect }: SidebarProps) {
  return (
    <aside className="sidebar">
      <div className="sidebar-brand">
        <span className="sidebar-eyebrow">控制台</span>
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




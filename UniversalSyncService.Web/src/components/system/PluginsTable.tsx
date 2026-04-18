import type { PluginSummary } from '../../api.ts';

type PluginsTableProps = {
  plugins: PluginSummary[];
};

export function PluginsTable({ plugins }: PluginsTableProps) {
  if (plugins.length === 0) {
    return <div className="empty-state">当前运行时未加载任何插件。</div>;
  }

  return (
    <table className="console-table">
      <thead>
        <tr>
          <th>插件 ID</th>
          <th>插件名称</th>
          <th>版本</th>
          <th>说明</th>
        </tr>
      </thead>
      <tbody>
        {plugins.map((plugin) => (
          <tr key={plugin.id}>
            <td className="mono">{plugin.id}</td>
            <td><strong>{plugin.name}</strong></td>
            <td className="mono">{plugin.version}</td>
            <td style={{ color: 'var(--text-muted)' }}>{plugin.description}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

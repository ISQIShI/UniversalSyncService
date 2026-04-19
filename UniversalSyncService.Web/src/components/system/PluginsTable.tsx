import type { PluginSummary } from '../../api.ts';
import { SystemPresentationHelpers } from './systemPresentation.ts';

type PluginsTableProps = {
  plugins: PluginSummary[];
};

export function PluginsTable({ plugins }: PluginsTableProps) {
  if (plugins.length === 0) {
    return <div className="empty-state">{SystemPresentationHelpers.getEmptyPluginsMessage()}</div>;
  }

  return (
    <table className="console-table">
      <thead>
        <tr>
          <th>{SystemPresentationHelpers.getPluginIdHeader()}</th>
          <th>{SystemPresentationHelpers.getPluginNameHeader()}</th>
          <th>{SystemPresentationHelpers.getPluginVersionHeader()}</th>
          <th>{SystemPresentationHelpers.getPluginDescriptionHeader()}</th>
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

import type { CreateOrUpdateNodeRequest } from '../../api.ts';
import { FormActions } from '../common/FormActions.tsx';
import type { NodeFormState } from './nodePresentation.ts';
import { parseKeyValueText, serializeKeyValueRecord } from './nodePresentation.ts';

type NodeFormProps = {
  formData: NodeFormState;
  submitting: boolean;
  isEditing: boolean;
  onSubmit: (event: React.FormEvent<HTMLFormElement>) => void;
  onCancel: () => void;
  setFormData: React.Dispatch<React.SetStateAction<NodeFormState | null>>;
};

export function toNodeRequestPayload(form: NodeFormState): CreateOrUpdateNodeRequest {
  const connectionSettings = parseKeyValueText(form.connectionSettingsText);
  if (form.rootPath.trim()) {
    connectionSettings.RootPath = form.rootPath.trim();
  }

  return {
    id: form.id.trim() || undefined,
    name: form.name.trim(),
    nodeType: form.nodeType.trim(),
    isEnabled: form.isEnabled,
    connectionSettings,
    customOptions: parseKeyValueText(form.customOptionsText),
  };
}

export function NodeForm({ formData, submitting, isEditing, onSubmit, onCancel, setFormData }: NodeFormProps) {
  const normalizedNodeType = formData.nodeType.trim().toLowerCase();
  const isOneDriveNode = normalizedNodeType === 'onedrive';
  const rootPathLabel = isOneDriveNode ? 'OneDrive 根目录' : '根路径';
  const rootPathPlaceholder = isOneDriveNode ? '例如 /Apps/UniversalSyncService' : '例如 D:/Sync/NodeA';

  return (
    <form className="plan-form" onSubmit={onSubmit}>
      <div className="form-group-grid">
        <label>
          <span>节点 ID</span>
          <input type="text" value={formData.id} onChange={(event) => setFormData((current) => current ? { ...current, id: event.target.value } : current)} disabled={isEditing} />
        </label>
        <label>
          <span>节点名称</span>
          <input type="text" value={formData.name} onChange={(event) => setFormData((current) => current ? { ...current, name: event.target.value } : current)} />
        </label>
        <label>
          <span>节点类型</span>
          <input type="text" value={formData.nodeType} onChange={(event) => setFormData((current) => current ? { ...current, nodeType: event.target.value } : current)} disabled={isEditing} />
        </label>
        <label>
          <span>{rootPathLabel}</span>
          <input
            type="text"
            value={formData.rootPath}
            onChange={(event) => setFormData((current) => {
              if (!current) return current;
              const nextConnectionSettings = parseKeyValueText(current.connectionSettingsText);
              if (event.target.value.trim()) {
                nextConnectionSettings.RootPath = event.target.value.trim();
              } else {
                delete nextConnectionSettings.RootPath;
              }

              return {
                ...current,
                rootPath: event.target.value,
                connectionSettingsText: serializeKeyValueRecord(nextConnectionSettings),
              };
            })}
            placeholder={rootPathPlaceholder}
          />
          {isOneDriveNode ? <small className="field-hint">OneDrive 根目录使用以 / 开头的远端绝对显示路径，例如 /Apps/UniversalSyncService；不能填写 D:/ 或 C:/ 这类本机绝对路径。</small> : null}
        </label>
        <label className="checkbox-label">
          <input type="checkbox" checked={formData.isEnabled} onChange={(event) => setFormData((current) => current ? { ...current, isEnabled: event.target.checked } : current)} />
          <span>启用节点</span>
        </label>
        <label className="full-width">
          <span>连接设置（每行 key=value）</span>
          <textarea value={formData.connectionSettingsText} onChange={(event) => setFormData((current) => current ? { ...current, connectionSettingsText: event.target.value, rootPath: parseKeyValueText(event.target.value).RootPath ?? current.rootPath } : current)} rows={6} />
        </label>
        <label className="full-width">
          <span>自定义选项（每行 key=value）</span>
          <textarea value={formData.customOptionsText} onChange={(event) => setFormData((current) => current ? { ...current, customOptionsText: event.target.value } : current)} rows={6} />
        </label>
      </div>

      <FormActions submitting={submitting} submitLabel="保存节点" onCancel={onCancel} />
    </form>
  );
}

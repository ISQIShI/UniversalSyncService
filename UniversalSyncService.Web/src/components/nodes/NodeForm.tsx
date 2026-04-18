import type { CreateOrUpdateNodeRequest } from '../../api.ts';
import { useI18n } from '../../i18n/useI18n.ts';
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
  const { t } = useI18n();
  const normalizedNodeType = formData.nodeType.trim().toLowerCase();
  const isOneDriveNode = normalizedNodeType === 'onedrive';
  const rootPathLabel = isOneDriveNode ? t('web.nodes.form.rootPathLabel.oneDrive') : t('web.nodes.form.rootPathLabel.default');
  const rootPathPlaceholder = isOneDriveNode ? t('web.nodes.form.rootPathPlaceholder.oneDrive') : t('web.nodes.form.rootPathPlaceholder.default');

  return (
    <form className="plan-form" onSubmit={onSubmit}>
      <div className="form-group-grid">
        <label>
          <span>{t('web.nodes.form.nodeId')}</span>
          <input type="text" value={formData.id} onChange={(event) => setFormData((current) => current ? { ...current, id: event.target.value } : current)} disabled={isEditing} />
        </label>
        <label>
          <span>{t('web.nodes.name')}</span>
          <input type="text" value={formData.name} onChange={(event) => setFormData((current) => current ? { ...current, name: event.target.value } : current)} />
        </label>
        <label>
          <span>{t('web.nodes.type')}</span>
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
          {isOneDriveNode ? <small className="field-hint">{t('web.nodes.form.oneDriveRootPathHint')}</small> : null}
        </label>
        <label className="checkbox-label">
          <input type="checkbox" checked={formData.isEnabled} onChange={(event) => setFormData((current) => current ? { ...current, isEnabled: event.target.checked } : current)} />
          <span>{t('web.nodes.form.enableNode')}</span>
        </label>
        <label className="full-width">
          <span>{t('web.nodes.form.connectionSettings')}</span>
          <textarea value={formData.connectionSettingsText} onChange={(event) => setFormData((current) => current ? { ...current, connectionSettingsText: event.target.value, rootPath: parseKeyValueText(event.target.value).RootPath ?? current.rootPath } : current)} rows={6} />
        </label>
        <label className="full-width">
          <span>{t('web.nodes.form.customOptions')}</span>
          <textarea value={formData.customOptionsText} onChange={(event) => setFormData((current) => current ? { ...current, customOptionsText: event.target.value } : current)} rows={6} />
        </label>
      </div>

      <FormActions submitting={submitting} submitLabel={t('web.nodes.form.saveNode')} submittingLabel={t('web.forms.submitting')} onCancel={onCancel} />
    </form>
  );
}

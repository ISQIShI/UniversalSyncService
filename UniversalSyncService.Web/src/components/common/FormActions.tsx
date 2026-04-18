type FormActionsProps = {
  submitting: boolean;
  submitLabel: string;
  submittingLabel?: string;
  onCancel: () => void;
  submitDisabled?: boolean;
};

export function FormActions({
  submitting,
  submitLabel,
  submittingLabel = '保存中…',
  onCancel,
  submitDisabled = false,
}: FormActionsProps) {
  return (
    <div className="form-actions">
      <button type="button" onClick={onCancel} disabled={submitting}>取消</button>
      <button type="submit" className="primary" disabled={submitting || submitDisabled}>
        {submitting ? submittingLabel : submitLabel}
      </button>
    </div>
  );
}

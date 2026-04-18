type FormActionsProps = {
  submitting: boolean;
  submitLabel: string;
  submittingLabel?: string;
  cancelLabel?: string;
  onCancel: () => void;
  submitDisabled?: boolean;
};

export function FormActions({
  submitting,
  submitLabel,
  submittingLabel = 'Saving…',
  cancelLabel = 'Cancel',
  onCancel,
  submitDisabled = false,
}: FormActionsProps) {
  return (
    <div className="form-actions">
      <button type="button" onClick={onCancel} disabled={submitting}>{cancelLabel}</button>
      <button type="submit" className="primary" disabled={submitting || submitDisabled}>
        {submitting ? submittingLabel : submitLabel}
      </button>
    </div>
  );
}

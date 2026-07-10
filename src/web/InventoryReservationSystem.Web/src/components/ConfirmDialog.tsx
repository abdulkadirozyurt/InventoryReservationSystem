interface ConfirmDialogProps {
  open: boolean;
  title: string;
  message: string;
  confirmLabel?: string;
  cancelLabel?: string;
  variant?: 'danger' | 'warning';
  onConfirm: () => void;
  onCancel: () => void;
  busy?: boolean;
}

export default function ConfirmDialog({
  open,
  title,
  message,
  confirmLabel = 'Confirm',
  cancelLabel = 'Cancel',
  variant = 'danger',
  onConfirm,
  onCancel,
  busy = false,
}: ConfirmDialogProps) {
  if (!open) return null;

  return (
    <div className="modal-backdrop" onClick={onCancel} role="presentation">
      <div className="modal" onClick={(e) => e.stopPropagation()}>
        <div className="modal-header">
          <h2>{title}</h2>
        </div>
        <div className="modal-body">
          <p>{message}</p>
        </div>
        <div className="modal-footer">
          <button
            type="button"
            className="btn"
            disabled={busy}
            onClick={onCancel}
          >
            {cancelLabel}
          </button>
          <button
            type="button"
            className={`btn btn--${variant}`}
            disabled={busy}
            onClick={onConfirm}
            autoFocus
          >
            {busy ? 'Processing…' : confirmLabel}
          </button>
        </div>
      </div>
    </div>
  );
}

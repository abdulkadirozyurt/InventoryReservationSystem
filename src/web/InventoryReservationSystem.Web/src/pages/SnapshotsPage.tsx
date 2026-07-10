import { FormEvent, useState } from 'react';

import Card from '../components/Card';
import ConfirmDialog from '../components/ConfirmDialog';
import ErrorBanner from '../components/ErrorBanner';
import { useCreateSnapshot, useRestoreSnapshot } from '../hooks/useInventory';
import { describeError } from '../hooks/useOrders';
import { errorCodeToUserMessage } from '../utils/errorMessages';
import { useToast } from '../hooks/useToast';

export default function SnapshotsPage() {
  const [requestedBy, setRequestedBy] = useState('demo-operator');
  const [snapshotId, setSnapshotId] = useState('');
  const [validation, setValidation] = useState<string | null>(null);
  const [restoreConfirmOpen, setRestoreConfirmOpen] = useState(false);
  const createM = useCreateSnapshot();
  const restoreM = useRestoreSnapshot();
  const createErr = createM.error ? describeError(createM.error) : null;
  const restoreErr = restoreM.error ? describeError(restoreM.error) : null;
  const { notify } = useToast();

  function createSnapshot(event: FormEvent) {
    event.preventDefault();
    const operator = requestedBy.trim();
    if (!operator) return setValidation('Requested by is required.');
    setValidation(null);
    createM.mutate({ requestedBy: operator }, {
      onSuccess: (result) => {
        if (result.snapshotId) setSnapshotId(result.snapshotId);
        if (result.success) notify('success', 'Snapshot oluşturuldu');
      },
    });
  }

  function confirmRestore() {
    const operator = requestedBy.trim();
    const id = snapshotId.trim();
    if (!id) return setValidation('Snapshot ID is required for restore.');
    if (!operator) return setValidation('Requested by is required.');
    setValidation(null);
    setRestoreConfirmOpen(true);
  }

  function doRestore() {
    restoreM.mutate(
      { snapshotId: snapshotId.trim(), body: { requestedBy: requestedBy.trim() } },
      {
        onSuccess: (result) => {
          if (result.success) notify('success', 'Snapshot geri yüklendi');
        },
      },
    );
    setRestoreConfirmOpen(false);
  }

  return (
    <div className="page-stack">
      <section className="page-heading">
        <p className="eyebrow">Snapshots</p>
        <h1>Create and restore inventory snapshots</h1>
        <p>
          Snapshot create and restore are real InventoryService operations. Snapshot history/list is not exposed by the backend yet,
          so this screen keeps the last created ID and lets you restore by explicit ID only.
        </p>
      </section>

      {validation && <ErrorBanner message={validation} variant="warning" />}
      {createErr && <ErrorBanner message={errorCodeToUserMessage(createErr.code, createErr.status, createErr.message)} code={createErr.code} />}
      {restoreErr && <ErrorBanner message={errorCodeToUserMessage(restoreErr.code, restoreErr.status, restoreErr.message)} code={restoreErr.code} />}

      <div className="split">
        <Card title="Create snapshot" subtitle="Capture current inventory state.">
          <form className="form-grid form-grid--single" onSubmit={createSnapshot}>
            <label>
              Requested by
              <input value={requestedBy} onChange={(e) => setRequestedBy(e.target.value)} />
            </label>
            <div className="form-actions form-actions--end">
              <button className="btn btn--primary" type="submit" disabled={createM.isPending}>
                {createM.isPending ? 'Oluşturuluyor…' : 'Create snapshot'}
              </button>
            </div>
          </form>

          {createM.data && (
            <div className="result-panel">
              <strong>{createM.data.success ? 'Snapshot created' : 'Snapshot failed'}</strong>
              {createM.data.snapshotId && <span>Snapshot ID: <code>{createM.data.snapshotId}</code></span>}
              {createM.data.errorMessage && <span>{errorCodeToUserMessage(createM.data.errorCode ?? 'Error', 0, createM.data.errorMessage)}</span>}
            </div>
          )}
        </Card>

        <Card title="Restore snapshot" subtitle="Restore by explicit snapshot ID.">
          <form className="form-grid form-grid--single" onSubmit={confirmRestore}>
            <label>
              Snapshot ID
              <input value={snapshotId} onChange={(e) => setSnapshotId(e.target.value)} placeholder="snapshot id" />
            </label>
            <label>
              Requested by
              <input value={requestedBy} onChange={(e) => setRequestedBy(e.target.value)} />
            </label>
            <div className="form-actions form-actions--end">
              <button className="btn btn--danger" type="submit" disabled={restoreM.isPending}>
                {restoreM.isPending ? 'Geri yükleniyor…' : 'Restore snapshot'}
              </button>
            </div>
          </form>

          {restoreM.data && (
            <div className="result-panel">
              <strong>{restoreM.data.success ? 'Restore completed' : 'Restore failed'}</strong>
              {restoreM.data.errorMessage
                ? <span>{errorCodeToUserMessage(restoreM.data.errorCode ?? 'Error', 0, restoreM.data.errorMessage)}</span>
                : <span>InventoryService accepted the restore request.</span>}
            </div>
          )}
        </Card>
      </div>

      <ConfirmDialog
        open={restoreConfirmOpen}
        title="Restore inventory snapshot"
        message="This will overwrite the current inventory state with the selected snapshot. Are you sure?"
        confirmLabel="Restore"
        cancelLabel="Cancel"
        variant="danger"
        onConfirm={doRestore}
        onCancel={() => setRestoreConfirmOpen(false)}
        busy={restoreM.isPending}
      />
    </div>
  );
}

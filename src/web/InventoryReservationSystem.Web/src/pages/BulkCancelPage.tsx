import { useMemo, useState } from 'react';

import Card from '../components/Card';
import ErrorBanner from '../components/ErrorBanner';
import { useBulkCancel, useOrderList, describeError } from '../hooks/useOrders';
import StatusBadge from '../components/StatusBadge';
import { Link } from 'react-router-dom';
import type { ListOrdersQuery, OrderStatus } from '../types/orders';

function parseLines(raw: string): string[] {
  return raw
    .split(/[\s,]+/)
    .map((s) => s.trim())
    .filter((s) => s.length > 0);
}

export default function BulkCancelPage() {
  const [rawText, setRawText] = useState('');
  const [reason, setReason] = useState('');
  const [selected, setSelected] = useState<Record<string, boolean>>({});

  const listQ = useOrderList(
    useMemo<ListOrdersQuery>(() => ({ status: 'Pending' as OrderStatus }), []),
  );
  const bulkM = useBulkCancel();

  const pendingOrders = listQ.data ?? [];
  const selectedNumbers = Object.entries(selected)
    .filter(([, v]) => v)
    .map(([k]) => k);

  function toggle(orderNumber: string) {
    setSelected((prev) => ({ ...prev, [orderNumber]: !prev[orderNumber] }));
  }

  function selectAllVisible() {
    const next: Record<string, boolean> = {};
    for (const o of pendingOrders) next[o.orderNumber] = true;
    setSelected(next);
  }
  function clearSelection() {
    setSelected({});
  }

  function submitFromText() {
    const ids = parseLines(rawText);
    if (ids.length === 0) {
      setRawText('');
      return;
    }
    bulkM.mutate(
      { orderNumbers: Array.from(new Set(ids)), reason: reason || undefined },
      {
        onSuccess: () => {
          setRawText('');
          void listQ.refetch();
        },
      },
    );
  }

  function submitSelected() {
    if (selectedNumbers.length === 0) return;
    bulkM.mutate(
      { orderNumbers: Array.from(new Set(selectedNumbers)), reason: reason || undefined },
      {
        onSuccess: () => {
          clearSelection();
          void listQ.refetch();
        },
      },
    );
  }

  const err = bulkM.error ? describeError(bulkM.error) : null;
  const results = bulkM.data?.results ?? [];

  return (
    <div className="page-stack">
      <Card
        title="Bulk cancel"
        subtitle="Cancel many pending orders in one request — idempotent per order."
        actions={<Link className="btn" to="/orders">← back to orders</Link>}
      >
        <p className="hint">
          Tip: the primary path is now checkbox selection on the{' '}
          <Link to="/orders">Orders</Link> page. This page stays for paste-by-list or
          pick-from-pending workflows.
        </p>
        {err && <ErrorBanner message={err.message} code={err.code} />}

        <label className="row">
          <span>Cancellation reason (optional, applies to all)</span>
          <input
            type="text"
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            placeholder="e.g. expired campaign"
          />
        </label>

        <div className="split">
          <section>
            <h3 className="section-title">Paste order numbers</h3>
            <textarea
              rows={6}
              placeholder={'One per line, comma or whitespace separated.\nORD-123\nORD-124'}
              value={rawText}
              onChange={(e) => setRawText(e.target.value)}
            />
            <button
              type="button"
              className="btn btn--danger"
              disabled={bulkM.isPending || parseLines(rawText).length === 0}
              onClick={submitFromText}
            >{bulkM.isPending ? 'Cancelling…' : 'Cancel listed'}</button>
          </section>

          <section>
            <h3 className="section-title">
              Or pick from pending
              <span style={{ float: 'right' }}>
                <button type="button" className="btn btn--small" onClick={selectAllVisible}>Pick all</button>
                {' '}
                <button type="button" className="btn btn--small" onClick={clearSelection}>Clear</button>
              </span>
            </h3>
            {listQ.isLoading ? (
              <p className="placeholder">Loading pending…</p>
            ) : pendingOrders.length === 0 ? (
              <p className="empty">No pending orders available.</p>
            ) : (
              <ul className="pick-list">
                {pendingOrders.map((o) => (
                  <li key={o.orderNumber}>
                    <label>
                      <input
                        type="checkbox"
                        checked={Boolean(selected[o.orderNumber])}
                        onChange={() => toggle(o.orderNumber)}
                      />
                      <span>
                        <Link to={`/orders/${o.orderNumber}`}>{o.orderNumber}</Link>
                        {' '}
                        <StatusBadge status={o.status} />
                      </span>
                    </label>
                  </li>
                ))}
              </ul>
            )}
            <button
              type="button"
              className="btn btn--danger"
              disabled={bulkM.isPending || selectedNumbers.length === 0}
              onClick={submitSelected}
            >Cancel selected ({selectedNumbers.length})</button>
          </section>
        </div>
      </Card>

      {results.length > 0 && (
        <Card title="Result">
          <table className="data-table">
            <thead>
              <tr>
                <th>Order #</th>
                <th>Success</th>
                <th>Code</th>
                <th>Message</th>
              </tr>
            </thead>
            <tbody>
              {results.map((r) => (
                <tr key={r.orderNumber}>
                  <td><code>{r.orderNumber}</code></td>
                  <td>{r.success ? 'Yes' : 'No'}</td>
                  <td>{r.errorCode ?? '—'}</td>
                  <td>{r.errorMessage ?? '—'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </Card>
      )}
    </div>
  );
}

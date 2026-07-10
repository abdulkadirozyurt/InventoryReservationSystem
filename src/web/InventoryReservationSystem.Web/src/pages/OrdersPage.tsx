import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';

import Card from '../components/Card';
import ErrorBanner from '../components/ErrorBanner';
import LoadingState from '../components/LoadingState';
import StatusBadge from '../components/StatusBadge';
import {
  useBulkCancel,
  useCancelOrder,
  useOrderList,
  describeError,
} from '../hooks/useOrders';
import { errorCodeToUserMessage } from '../utils/errorMessages';
import { useToast } from '../hooks/useToast';
import type {
  BulkCancelOrdersResponse,
  ListOrdersQuery,
  Order,
  OrderStatus,
} from '../types/orders';

const STATUS_OPTIONS: OrderStatus[] = ['Pending', 'Confirmed', 'Cancelled', 'Expired'];

function todayIso(): string {
  return new Date().toISOString().slice(0, 10);
}

function daysAgoIso(days: number): string {
  return new Date(Date.now() - days * 24 * 60 * 60 * 1000).toISOString().slice(0, 10);
}

export default function OrdersPage() {
  const [status, setStatus] = useState<OrderStatus | ''>('');
  const [from, setFrom] = useState(daysAgoIso(7));
  const [to, setTo] = useState(todayIso());
  const [selected, setSelected] = useState<Record<string, boolean>>({});
  const [reason, setReason] = useState('');
  const [result, setResult] = useState<BulkCancelOrdersResponse | null>(null);

  const query = useMemo<ListOrdersQuery>(() => {
    const q: ListOrdersQuery = {};
    if (status) q.status = status;
    if (from) q.from = new Date(from + 'T00:00:00Z').toISOString();
    if (to) q.to = new Date(to + 'T23:59:59Z').toISOString();
    return q;
  }, [status, from, to]);

  const listQ = useOrderList(query);
  const bulkM = useBulkCancel();
  const { notify } = useToast();

  const orders: Order[] = listQ.data ?? [];

  if (listQ.error) {
    const { code, message, status } = describeError(listQ.error);
    return <ErrorBanner message={errorCodeToUserMessage(code, status, message)} code={code} />;
  }
  if (listQ.isLoading) return <LoadingState label="Loading orders…" />;

  const pendingOrders = orders.filter((o) => o.status === 'Pending');
  const selectedNumbers = Object.entries(selected)
    .filter(([, v]) => v)
    .map(([k]) => k);
  const dedupedSelected = Array.from(new Set(selectedNumbers));
  const allPendingSelected =
    pendingOrders.length > 0 && pendingOrders.every((o) => selected[o.orderNumber]);

  function toggle(orderNumber: string) {
    setSelected((prev) => ({ ...prev, [orderNumber]: !prev[orderNumber] }));
  }

  function selectAllPending() {
    const next: Record<string, boolean> = {};
    for (const o of pendingOrders) next[o.orderNumber] = true;
    setSelected(next);
  }

  function clearSelection() {
    setSelected({});
  }

  function submitBulkCancel() {
    if (dedupedSelected.length === 0) return;
    const n = dedupedSelected.length;
    const ok = window.confirm(
      `Cancel ${n} pending order${n === 1 ? '' : 's'}? This is idempotent.`,
    );
    if (!ok) return;
    bulkM.mutate(
      { orderNumbers: dedupedSelected, reason: reason.trim() || undefined },
      {
        onSuccess: (resp) => {
          setResult(resp);
          setSelected({});
          setReason('');
          notify('success', 'Seçili siparişler iptal edildi');
          void listQ.refetch();
        },
      },
    );
  }

  return (
    <div className="page-stack">
      <Card
        title="Orders"
        subtitle="Create, confirm, cancel multi-SKU batch reservations."
        actions={
          <>
            <Link className="btn" to="/orders/bulk-cancel">Bulk cancel (by list)</Link>
            <Link className="btn btn--primary" to="/orders/new">+ New order</Link>
          </>
        }
      >
        <form
          className="filter-bar"
          onSubmit={(e) => { e.preventDefault(); }}
        >
          <label>
            Status
            <select value={status} onChange={(e) => setStatus(e.target.value as OrderStatus | '')}>
              <option value="">All</option>
              {STATUS_OPTIONS.map((s) => <option key={s} value={s}>{s}</option>)}
            </select>
          </label>
          <label>
            From
            <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
          </label>
          <label>
            To
            <input type="date" value={to} onChange={(e) => setTo(e.target.value)} />
          </label>
          <button
            type="button"
            className="btn"
            onClick={() => { void listQ.refetch(); }}
          >Refresh</button>
        </form>

        <div className="bulk-bar">
          <button
            type="button"
            className="btn btn--small"
            onClick={selectAllPending}
            disabled={pendingOrders.length === 0}
          >Select all pending</button>
          <button
            type="button"
            className="btn btn--small"
            onClick={clearSelection}
            disabled={selectedNumbers.length === 0}
          >Clear ({selectedNumbers.length})</button>
          <input
            type="text"
            placeholder="Bulk-cancel reason (optional)"
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            style={{ flex: 1, minWidth: 160 }}
          />
          <button
            type="button"
            className="btn btn--danger"
            disabled={bulkM.isPending || dedupedSelected.length === 0}
            onClick={submitBulkCancel}
          >{bulkM.isPending ? 'Cancelling…' : `Cancel selected (${dedupedSelected.length})`}</button>
        </div>

        {orders.length === 0 ? (
          <p className="empty">Filtrelere uyan sipariş bulunamadı</p>
        ) : (
          <table className="data-table">
            <thead>
              <tr>
                <th style={{ width: 32 }}>
                  <input
                    type="checkbox"
                    aria-label="Select all pending visible"
                    checked={allPendingSelected}
                    disabled={pendingOrders.length === 0}
                    onChange={(e) => {
                      if (e.target.checked) selectAllPending();
                      else clearSelection();
                    }}
                  />
                </th>
                <th>Order #</th>
                <th>Status</th>
                <th>Items</th>
                <th>Requested</th>
                <th>Reserved</th>
                <th>Created</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {orders.map((o) => {
                const requested = o.items.reduce((s, i) => s + i.requestedQuantity, 0);
                const reserved = o.items.reduce((s, i) => s + i.reservedQuantity, 0);
                const canCancel = o.status === 'Pending';
                const isSelected = Boolean(selected[o.orderNumber]);
                return (
                  <tr key={o.orderNumber} className={isSelected ? 'row--selected' : undefined}>
                    <td>
                      <input
                        type="checkbox"
                        aria-label={`Select ${o.orderNumber}`}
                        checked={isSelected}
                        disabled={!canCancel}
                        onChange={() => toggle(o.orderNumber)}
                      />
                    </td>
                    <td><Link to={`/orders/${o.orderNumber}`}>{o.orderNumber}</Link></td>
                    <td><StatusBadge status={o.status} /></td>
                    <td>{o.items.length}</td>
                    <td>{requested}</td>
                    <td>{reserved}</td>
                    <td>{new Date(o.createdAt).toLocaleString()}</td>
                    <td>
                      <Link className="btn btn--small" to={`/orders/${o.orderNumber}`}>Open</Link>
                      {canCancel && (
                        <span className="per-row-cancel">
                          <PerRowCancel orderNumber={o.orderNumber} onDone={() => void listQ.refetch()} />
                        </span>
                      )}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        )}
      </Card>

      {result && (
        <Card
          title={`Bulk cancel result (${result.results.length})`}
          subtitle={`${result.results.filter((r) => r.success).length} ok, ${result.results.filter((r) => !r.success).length} failed`}
          actions={
            <button
              type="button"
              className="btn btn--small"
              onClick={() => setResult(null)}
            >Dismiss</button>
          }
        >
          <table className="data-table">
            <thead>
              <tr>
                <th>Order #</th>
                <th>Success</th>
                <th>Message</th>
              </tr>
            </thead>
            <tbody>
              {result.results.map((r) => (
                <tr key={r.orderNumber}>
                  <td><code>{r.orderNumber}</code></td>
                  <td>{r.success ? 'Yes' : 'No'}</td>
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

/** Inner row-level cancel button: uses a dedicated mutation bound to this row. */
function PerRowCancel({ orderNumber, onDone }: { orderNumber: string; onDone: () => void }) {
  const m = useCancelOrder(orderNumber);
  const [err, setErr] = useState<string | null>(null);
  return (
    <>
      <button
        className="btn btn--small btn--danger"
        disabled={m.isPending}
        onClick={() => {
          setErr(null);
          m.mutate(
            { reason: 'Cancelled from list' },
            {
              onSuccess: () => onDone(),
              onError: (e) => setErr(describeError(e).message),
            },
          );
        }}
      >{m.isPending ? 'İptal ediliyor…' : 'Cancel'}</button>
      {err && <span className="inline-error" title={err}>!</span>}
    </>
  );
}

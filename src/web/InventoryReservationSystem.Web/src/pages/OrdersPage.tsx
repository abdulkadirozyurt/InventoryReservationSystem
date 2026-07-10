import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';

import Card from '../components/Card';
import ErrorBanner from '../components/ErrorBanner';
import LoadingState from '../components/LoadingState';
import StatusBadge from '../components/StatusBadge';
import {
  useCancelOrder,
  useOrderList,
  describeError,
} from '../hooks/useOrders';
import type { ListOrdersQuery, OrderStatus } from '../types/orders';

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

  const query = useMemo<ListOrdersQuery>(() => {
    const q: ListOrdersQuery = {};
    if (status) q.status = status;
    if (from) q.from = new Date(from + 'T00:00:00Z').toISOString();
    if (to) q.to = new Date(to + 'T23:59:59Z').toISOString();
    return q;
  }, [status, from, to]);

  const listQ = useOrderList(query);

  const orders = listQ.data ?? [];

  if (listQ.error) {
    const { code, message } = describeError(listQ.error);
    return <ErrorBanner message={message} code={code} />;
  }
  if (listQ.isLoading) return <LoadingState label="Loading orders…" />;

  return (
    <div className="page-stack">
      <Card
        title="Orders"
        subtitle="Create, confirm, cancel multi-SKU batch reservations."
        actions={
          <>
            <Link className="btn" to="/orders/bulk-cancel">Bulk cancel</Link>
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

        {orders.length === 0 ? (
          <p className="empty">No orders match the current filter.</p>
        ) : (
          <table className="data-table">
            <thead>
              <tr>
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
                return (
                  <tr key={o.orderNumber}>
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
      >{m.isPending ? '…' : 'Cancel'}</button>
      {err && <span className="inline-error" title={err}>!</span>}
    </>
  );
}

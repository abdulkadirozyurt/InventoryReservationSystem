import { useMemo } from 'react';
import { Link } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';

import Card from '../components/Card';
import ErrorBanner from '../components/ErrorBanner';
import LoadingState from '../components/LoadingState';
import StatTile from '../components/StatTile';
import StatusBadge from '../components/StatusBadge';
import { ordersApi } from '../api/orders';
import { healthApi } from '../api/health';
import type { OrderAnalytics } from '../types/orders';
import { describeError } from '../hooks/useOrders';

function isoDaysAgo(days: number): string {
  return new Date(Date.now() - days * 24 * 60 * 60 * 1000).toISOString();
}

function fmtPercent(v: number): string {
  return `${(v * 100).toFixed(1)}%`;
}

function fmtSeconds(v: number): string {
  return `${v.toFixed(2)}s`;
}

export default function HomePage() {
  const toIso = useMemo(() => new Date().toISOString(), []);
  const fromIso = useMemo(() => isoDaysAgo(7), []);

  const ordersQ = useQuery({
    queryKey: ['orders', 'list', 'recent'],
    queryFn: ({ signal }) => ordersApi.list({}, signal),
  });
  const analyticsQ = useQuery({
    queryKey: ['orders', 'analytics', fromIso, toIso],
    queryFn: ({ signal }) => ordersApi.analytics(fromIso, toIso, signal),
  });
  const liveQ = useQuery({
    queryKey: ['health', 'live'],
    queryFn: ({ signal }) => healthApi.live(signal),
    refetchInterval: 30_000,
  });

  const analytics: OrderAnalytics | undefined = analyticsQ.data;
  const orders = ordersQ.data ?? [];
  const recent = orders.slice(0, 5);

  if (ordersQ.error || analyticsQ.error) {
    const err = ordersQ.error ?? analyticsQ.error;
    const { code, message } = describeError(err);
    return <ErrorBanner message={message} code={code} />;
  }
  if (ordersQ.isLoading || analyticsQ.isLoading) {
    return <LoadingState label="Loading dashboard…" />;
  }

  const successTone =
    analytics && analytics.successRatio >= 0.9 ? 'success'
    : analytics && analytics.successRatio >= 0.7 ? 'warning'
    : 'danger';

  return (
    <div className="page-stack">
      <Card
        title="Overview"
        subtitle="Last 7 days — analytical snapshot of reservation health."
        actions={
          <>
            <Link className="btn" to="/orders">All orders</Link>
            <Link className="btn btn--primary" to="/orders/new">+ New order</Link>
          </>
        }
      >
        <div className="kpi-grid">
          <StatTile
            label="Success ratio"
            value={analytics ? fmtPercent(analytics.successRatio) : '—'}
            hint={analytics ? `Failure ${fmtPercent(analytics.failureRatio ?? 1 - analytics.successRatio)}` : ''}
            tone={successTone}
          />
          <StatTile
            label="Total orders"
            value={analytics?.totalOrdersFound ?? orders.length}
            hint="Found in window"
          />
          <StatTile
            label="Reservation density"
            value={analytics ? fmtPercent(analytics.reservationDensity) : '—'}
            hint="Reserved / requested"
          />
          <StatTile
            label="Avg fulfillment"
            value={analytics ? fmtSeconds(analytics.averageFulfillmentDurationSeconds) : '—'}
            hint="Confirm-to-fulfill"
          />
        </div>

        <div className="banner banner--info" style={{ marginTop: '1rem' }}>
          Backend probe: <strong>{liveQ.data?.status ?? '…'}</strong>
          {' · '}
          <Link to="/health">Open health →</Link>
        </div>
      </Card>

      <Card title="Recent orders" actions={<Link className="btn" to="/orders">View all →</Link>}>
        {recent.length === 0 ? (
          <p className="empty">No orders yet. Create the first one.</p>
        ) : (
          <table className="data-table">
            <thead>
              <tr>
                <th>Order #</th>
                <th>Status</th>
                <th>Items</th>
                <th>Reserved</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {recent.map((o) => {
                const requested = o.items.reduce((s, i) => s + i.requestedQuantity, 0);
                const reserved = o.items.reduce((s, i) => s + i.reservedQuantity, 0);
                return (
                  <tr key={o.orderNumber}>
                    <td><Link to={`/orders/${o.orderNumber}`}>{o.orderNumber}</Link></td>
                    <td><StatusBadge status={o.status} /></td>
                    <td>{o.items.length}</td>
                    <td>{reserved}/{requested}</td>
                    <td><Link to={`/orders/${o.orderNumber}`}>Open →</Link></td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        )}
      </Card>

      <Card title="Quick actions">
        <div className="quick-actions">
          <Link className="btn btn--primary" to="/orders/new">Create order</Link>
          <Link className="btn" to="/orders/bulk-cancel">Bulk cancel</Link>
          <Link className="btn" to="/health">Backend health</Link>
        </div>
      </Card>
    </div>
  );
}

import { useMemo } from 'react';
import { Link } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';

import Card from '../components/Card';
import ErrorBanner from '../components/ErrorBanner';
import LoadingState from '../components/LoadingState';
import StatTile from '../components/StatTile';
import StatusBadge from '../components/StatusBadge';
import { ordersApi } from '../api/orders';
import { describeError, useOrderList } from '../hooks/useOrders';
import type { OrderAnalytics, OrderStatus } from '../types/orders';

function isoDaysAgo(days: number): string {
  return new Date(Date.now() - days * 24 * 60 * 60 * 1000).toISOString();
}

function fmtPercent(value: number): string {
  return `${Math.round(value * 100)}%`;
}

function fmtSeconds(value: number): string {
  if (value < 60) return `${Math.round(value)}s`;
  return `${Math.round(value / 60)}m`;
}

function summarize(analytics?: OrderAnalytics) {
  if (!analytics || analytics.totalOrdersFound === 0) {
    return {
      tone: 'neutral' as const,
      title: 'No order activity in the last 7 days',
      text: 'Create a demo order to populate reservation metrics and recent workflow activity.',
    };
  }

  if (analytics.failureRatio > analytics.successRatio) {
    return {
      tone: 'danger' as const,
      title: 'Reservation failures dominate',
      text: `${fmtPercent(analytics.failureRatio)} of recent orders could not reserve requested stock. Check SKU and warehouse stock coverage.`,
    };
  }

  if (analytics.successRatio < 0.9) {
    return {
      tone: 'warning' as const,
      title: 'System is working, but contention exists',
      text: `${fmtPercent(analytics.successRatio)} success ratio. Review failed lines before a demo or load test.`,
    };
  }

  return {
    tone: 'success' as const,
    title: 'Reservation flow is healthy',
    text: `${fmtPercent(analytics.successRatio)} success ratio across recent orders. OrderService and InventoryService workflows are producing usable results.`,
  };
}

const STATUS_ORDER: OrderStatus[] = ['Pending', 'Confirmed', 'Cancelled', 'Expired'];

export default function HomePage() {
  const from = useMemo(() => isoDaysAgo(7), []);
  const to = useMemo(() => new Date().toISOString(), []);
  const ordersQ = useOrderList({ from, to });
  const analyticsQ = useQuery({
    queryKey: ['orders', 'analytics', from, to],
    queryFn: ({ signal }) => ordersApi.analytics(from, to, signal),
  });

  if (ordersQ.error || analyticsQ.error) {
    const err = ordersQ.error ?? analyticsQ.error;
    const { code, message } = describeError(err);
    return <ErrorBanner message={message} code={code} />;
  }

  if (ordersQ.isLoading || analyticsQ.isLoading) {
    return <LoadingState label="Loading overview…" />;
  }

  const orders = ordersQ.data ?? [];
  const analytics = analyticsQ.data;
  const recent = orders.slice(0, 5);
  const summary = summarize(analytics);
  const counts = STATUS_ORDER.map((status) => ({
    status,
    count: orders.filter((order) => order.status === status).length,
  }));
  const reservedUnits = orders.reduce(
    (sum, order) => sum + order.items.reduce((inner, item) => inner + item.reservedQuantity, 0),
    0,
  );
  const requestedUnits = orders.reduce(
    (sum, order) => sum + order.items.reduce((inner, item) => inner + item.requestedQuantity, 0),
    0,
  );

  return (
    <div className="page-stack">
      <section className={`overview-banner overview-banner--${summary.tone}`}>
        <p className="eyebrow">Overview</p>
        <h1>{summary.title}</h1>
        <p>{summary.text}</p>
        <div className="metric-row">
          <span>{orders.length} recent orders</span>
          <span>{requestedUnits} requested units</span>
          <span>{reservedUnits} reserved units</span>
        </div>
      </section>

      <div className="kpi-grid">
        <StatTile
          label="Orders found"
          value={analytics?.totalOrdersFound ?? 0}
          hint="Last 7 days"
        />
        <StatTile
          label="Success ratio"
          value={analytics ? fmtPercent(analytics.successRatio) : '—'}
          hint={analytics ? `Failure ${fmtPercent(analytics.failureRatio)}` : ''}
          tone={analytics && analytics.successRatio >= 0.9 ? 'success' : analytics && analytics.successRatio >= 0.7 ? 'warning' : 'danger'}
        />
        <StatTile
          label="Reservation density"
          value={analytics ? analytics.reservationDensity.toFixed(2) : '—'}
          hint="Reserved lines per order"
        />
        <StatTile
          label="Avg fulfillment"
          value={analytics ? fmtSeconds(analytics.averageFulfillmentDurationSeconds) : '—'}
          hint="Create → confirm/cancel"
        />
      </div>

      <Card title="Order status mix" subtitle="Real OrderService list data for the selected 7-day window.">
        <div className="status-mix">
          {counts.map(({ status, count }) => (
            <div className="status-mix__item" key={status}>
              <StatusBadge status={status} />
              <strong>{count}</strong>
            </div>
          ))}
        </div>
      </Card>

      <Card title="Recent orders" subtitle="Newest orders returned by OrderService.">
        {recent.length === 0 ? (
          <p className="empty">No recent orders. Create an order to test the reservation flow.</p>
        ) : (
          <table className="data-table">
            <thead>
              <tr>
                <th>Order #</th>
                <th>Status</th>
                <th>Lines</th>
                <th>Updated</th>
              </tr>
            </thead>
            <tbody>
              {recent.map((order) => (
                <tr key={order.orderNumber}>
                  <td><Link to={`/orders/${order.orderNumber}`}>{order.orderNumber}</Link></td>
                  <td><StatusBadge status={order.status} /></td>
                  <td>{order.items.length}</td>
                  <td>{new Date(order.updatedAt).toLocaleString()}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Card>

      <Card title="Operational shortcuts" subtitle="User-facing workflows only. Technical health stays out of the menu.">
        <div className="quick-actions">
          <Link className="btn btn--primary" to="/orders/new">Create order</Link>
          <Link className="btn" to="/orders">Browse orders</Link>
          <Link className="btn" to="/inventory">Inventory lookup</Link>
          <Link className="btn" to="/inventory/transfers">Transfer stock</Link>
          <Link className="btn" to="/inventory/snapshots">Snapshots</Link>
        </div>
      </Card>
    </div>
  );
}

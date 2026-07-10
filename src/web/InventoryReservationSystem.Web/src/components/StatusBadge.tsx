import type { OrderStatus } from '../types/orders';

interface StatusBadgeProps {
  status: OrderStatus;
}

const TONE: Record<OrderStatus, string> = {
  Pending: 'badge--warning',
  Confirmed: 'badge--success',
  Cancelled: 'badge--muted',
  Expired: 'badge--danger',
};

export default function StatusBadge({ status }: StatusBadgeProps) {
  return <span className={`badge ${TONE[status]}`}>{status}</span>;
}

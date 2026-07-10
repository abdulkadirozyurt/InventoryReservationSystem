import { useState } from 'react';
import { Link, useParams } from 'react-router-dom';

import Card from '../components/Card';
import ErrorBanner from '../components/ErrorBanner';
import LoadingState from '../components/LoadingState';
import StatusBadge from '../components/StatusBadge';
import {
  useCancelOrder,
  useConfirmOrder,
  useOrder,
  describeError,
} from '../hooks/useOrders';
import { errorCodeToUserMessage } from '../utils/errorMessages';
import { useToast } from '../hooks/useToast';

export default function OrderDetailPage() {
  const { orderNumber = '' } = useParams<{ orderNumber: string }>();
  const orderQ = useOrder(orderNumber);
  const confirmM = useConfirmOrder(orderNumber);
  const cancelM = useCancelOrder(orderNumber);
  const [cancelReason, setCancelReason] = useState('');
  const { notify } = useToast();

  if (orderQ.isLoading) return <LoadingState label={`Loading ${orderNumber}…`} />;
  if (orderQ.error) {
    const { code, message, status } = describeError(orderQ.error);
    if (status === 404 || code === 'HttpError' && message.toLowerCase().includes('not found')) {
      return <ErrorBanner message={`Order ${orderNumber} not found.`} code={code} variant="warning" />;
    }
    return <ErrorBanner message={errorCodeToUserMessage(code, status, message)} code={code} />;
  }

  const order = orderQ.data;
  if (!order) return <ErrorBanner message="Order not found." variant="warning" />;

  const requested = order.items.reduce((s, i) => s + i.requestedQuantity, 0);
  const reserved = order.items.reduce((s, i) => s + i.reservedQuantity, 0);
  const canConfirm = order.status === 'Pending';
  const canCancel = order.status === 'Pending';

  const confirmErr = confirmM.error ? describeError(confirmM.error) : null;
  const cancelErr = cancelM.error ? describeError(cancelM.error) : null;

  return (
    <div className="page-stack">
      <Card
        title={`Order ${order.orderNumber}`}
        subtitle={`Created ${new Date(order.createdAt).toLocaleString()} · Updated ${new Date(order.updatedAt).toLocaleString()}`}
        actions={<Link className="btn" to="/orders">← back to list</Link>}
      >
        <div className="meta-row">
          <div><strong>Status:</strong> <StatusBadge status={order.status} /></div>
          {order.reservationId && (
            <div><strong>Reservation id:</strong> <code>{order.reservationId}</code></div>
          )}
          <div><strong>Reserved:</strong> {reserved}/{requested}</div>
        </div>

        <h3 className="section-title">Items</h3>
        {order.items.length === 0 ? (
          <p className="empty">No items.</p>
        ) : (
          <table className="data-table">
            <thead>
              <tr>
                <th>SKU</th>
                <th>Warehouse</th>
                <th>Requested</th>
                <th>Reserved</th>
              </tr>
            </thead>
            <tbody>
              {order.items.map((it, idx) => (
                <tr key={idx}>
                  <td><code>{it.sku}</code></td>
                  <td>{it.warehouseId}</td>
                  <td>{it.requestedQuantity}</td>
                  <td>{it.reservedQuantity}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Card>

      <Card title="Actions">
        <div className="action-row">
          <div className="action-group">
            <button
              className="btn btn--primary"
              disabled={!canConfirm || confirmM.isPending}
              onClick={() => confirmM.mutate(undefined, {
                onSuccess: (resp) => {
                  if (resp.success) notify('success', 'Sipariş onaylandı');
                },
                onError: (e) => { void e; /* surfaced via confirmErr */ },
              })}
            >
              {confirmM.isPending ? 'Onaylanıyor…' : canConfirm ? 'Confirm order' : 'Already ' + order.status}
            </button>
            {confirmErr && <ErrorBanner message={errorCodeToUserMessage(confirmErr.code, confirmErr.status, confirmErr.message)} code={confirmErr.code} variant="warning" />}
            {confirmM.data && !confirmM.data.success && (
              <ErrorBanner message={errorCodeToUserMessage(confirmM.data.errorCode ?? 'Refused', 0, confirmM.data.errorMessage ?? 'Confirm refused')} code={confirmM.data.errorCode ?? 'Refused'} />
            )}
          </div>

          <div className="action-group">
            <label htmlFor="cancel-reason">Cancel reason</label>
            <input
              id="cancel-reason"
              type="text"
              placeholder="Optional"
              value={cancelReason}
              onChange={(e) => setCancelReason(e.target.value)}
              disabled={!canCancel}
            />
            <button
              className="btn btn--danger"
              disabled={!canCancel || cancelM.isPending}
              onClick={() => cancelM.mutate({ reason: cancelReason || undefined }, {
                onSuccess: (resp) => {
                  if (resp.success) notify('success', 'Sipariş iptal edildi');
                },
              })}
            >
              {cancelM.isPending ? 'İptal ediliyor…' : canCancel ? 'Cancel order' : 'Cannot cancel from ' + order.status}
            </button>
            {cancelErr && <ErrorBanner message={errorCodeToUserMessage(cancelErr.code, cancelErr.status, cancelErr.message)} code={cancelErr.code} variant="warning" />}
            {cancelM.data && !cancelM.data.success && (
              <ErrorBanner message={errorCodeToUserMessage(cancelM.data.errorCode ?? 'Refused', 0, cancelM.data.errorMessage ?? 'Cancel refused')} code={cancelM.data.errorCode ?? 'Refused'} />
            )}
          </div>
        </div>
      </Card>

      <p style={{ textAlign: 'center' }}>
        <Link to="/orders">← All orders</Link>
        {' · '}
        <Link to="/orders/bulk-cancel">Bulk cancel →</Link>
      </p>
    </div>
  );
}

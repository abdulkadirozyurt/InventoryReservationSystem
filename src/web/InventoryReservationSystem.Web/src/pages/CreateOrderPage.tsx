import { useState } from 'react';
import { useNavigate } from 'react-router-dom';

import Card from '../components/Card';
import ErrorBanner from '../components/ErrorBanner';
import { useCreateOrder, describeError } from '../hooks/useOrders';
import type { CreateOrderItemRequest } from '../types/orders';

interface RowDraft extends CreateOrderItemRequest {
  /** local-only key for React lists */
  key: string;
}

function uid(): string {
  return Math.random().toString(36).slice(2, 10);
}

function makeRow(sku = '', warehouseId = '', quantity = 1): RowDraft {
  return { key: uid(), sku, warehouseId, quantity };
}

function genIdempotencyKey(): string {
  // RFC4122 v4 — backend accepts any non-empty string but we mirror its convention.
  return crypto.randomUUID();
}

export default function CreateOrderPage() {
  const nav = useNavigate();
  const [rows, setRows] = useState<RowDraft[]>([makeRow(), makeRow()]);
  const [idemKey, setIdemKey] = useState<string>(genIdempotencyKey());
  const [validationError, setValidationError] = useState<string | null>(null);
  const createM = useCreateOrder();

  function updateRow(key: string, patch: Partial<RowDraft>) {
    setRows((prev) => prev.map((r) => (r.key === key ? { ...r, ...patch } : r)));
  }
  function addRow() {
    setRows((prev) => [...prev, makeRow()]);
  }
  function removeRow(key: string) {
    setRows((prev) => (prev.length <= 1 ? prev : prev.filter((r) => r.key !== key)));
  }

  function submit() {
    setValidationError(null);

    const cleanItems: CreateOrderItemRequest[] = [];
    for (const r of rows) {
      const sku = r.sku.trim();
      const warehouse = r.warehouseId.trim();
      const qty = Number(r.quantity);
      if (!sku) return setValidationError('Every item needs a SKU.');
      if (!warehouse) return setValidationError('Every item needs a warehouse id.');
      if (!Number.isFinite(qty) || qty <= 0) {
        return setValidationError('Quantity must be a positive integer.');
      }
      cleanItems.push({ sku, warehouseId: warehouse, quantity: Math.trunc(qty) });
    }
    if (!idemKey.trim()) {
      return setValidationError('Idempotency-Key is required.');
    }

    createM.mutate(
      { body: { items: cleanItems }, idempotencyKey: idemKey.trim() },
      {
        onSuccess: (resp) => {
          if (resp.success && resp.orderNumber) {
            nav(`/orders/${encodeURIComponent(resp.orderNumber)}`);
          }
        },
      },
    );
  }

  const err = createM.error ? describeError(createM.error) : null;

  return (
    <div className="page-stack">
      <Card
        title="New order"
        subtitle="All-or-nothing multi-SKU reservation. Send the same Idempotency-Key to retry safely."
      >
        {validationError && <ErrorBanner message={validationError} variant="warning" />}
        {err && <ErrorBanner message={err.message} code={err.code} />}
        {createM.data && !createM.data.success && createM.data.failures.length > 0 && (
          <Card title="Partial failures" >
            <ul className="failure-list">
              {createM.data.failures.map((f, i) => (
                <li key={i}>
                  <code>{f.sku}</code> @ <code>{f.warehouseId}</code> — <strong>{f.errorCode}</strong>: {f.reason}
                </li>
              ))}
            </ul>
          </Card>
        )}

        <label className="row">
          <span>Idempotency-Key</span>
          <div className="idem-row">
            <input
              type="text"
              value={idemKey}
              onChange={(e) => setIdemKey(e.target.value)}
              spellCheck={false}
              autoComplete="off"
            />
            <button
              type="button"
              className="btn"
              onClick={() => setIdemKey(genIdempotencyKey())}
            >Regenerate</button>
          </div>
          <small className="hint">Backend rejects empty keys and conflicting bodies.</small>
        </label>

        <h3 className="section-title">Items</h3>
        <table className="data-table">
          <thead>
            <tr>
              <th>SKU</th>
              <th>Warehouse</th>
              <th>Qty</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.key}>
                <td>
                  <input
                    type="text"
                    value={r.sku}
                    onChange={(e) => updateRow(r.key, { sku: e.target.value })}
                    placeholder="SKU-001"
                  />
                </td>
                <td>
                  <input
                    type="text"
                    value={r.warehouseId}
                    onChange={(e) => updateRow(r.key, { warehouseId: e.target.value })}
                    placeholder="WH-A"
                  />
                </td>
                <td style={{ minWidth: 80 }}>
                  <input
                    type="number"
                    min={1}
                    value={Number.isFinite(r.quantity) ? r.quantity : ''}
                    onChange={(e) => updateRow(r.key, { quantity: Number(e.target.value) })}
                  />
                </td>
                <td>
                  <button
                    type="button"
                    className="btn btn--small"
                    onClick={() => removeRow(r.key)}
                    disabled={rows.length <= 1}
                  >Remove</button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>

        <div className="action-row" style={{ marginTop: '1rem' }}>
          <button type="button" className="btn" onClick={addRow}>+ Add row</button>
          <button
            type="button"
            className="btn btn--primary"
            disabled={createM.isPending}
            onClick={submit}
          >{createM.isPending ? 'Submitting…' : 'Submit order'}</button>
        </div>
      </Card>
    </div>
  );
}

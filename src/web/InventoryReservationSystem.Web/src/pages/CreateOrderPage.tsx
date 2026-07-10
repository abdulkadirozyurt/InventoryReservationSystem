import { useState } from 'react';
import { useNavigate } from 'react-router-dom';

import Card from '../components/Card';
import ErrorBanner from '../components/ErrorBanner';
import LoadingState from '../components/LoadingState';
import { useCreateOrder, describeError } from '../hooks/useOrders';
import { errorCodeToUserMessage } from '../utils/errorMessages';
import { useInventoryCatalogue } from '../hooks/useInventory';
import { useToast } from '../hooks/useToast';
import type { CreateOrderItemRequest } from '../types/orders';

interface RowDraft extends CreateOrderItemRequest {
  key: string;
}

function uid(): string {
  return Math.random().toString(36).slice(2, 10);
}

function makeRow(sku = '', warehouseId = '', quantity = 1): RowDraft {
  return { key: uid(), sku, warehouseId, quantity };
}

function genIdempotencyKey(): string {
  return crypto.randomUUID();
}

export default function CreateOrderPage() {
  const nav = useNavigate();
  const [rows, setRows] = useState<RowDraft[]>([makeRow(), makeRow()]);
  const [idemKey] = useState<string>(genIdempotencyKey());
  const [validationError, setValidationError] = useState<string | null>(null);
  const createM = useCreateOrder();
  const { notify } = useToast();

  const catalogueQ = useInventoryCatalogue();
  const catalogue = catalogueQ.data;
  const skuOptions = [...new Set((catalogue ?? []).map(c => c.sku))].sort();

  function getWarehouseOptionsForSku(sku: string): string[] {
    if (!sku || !catalogue) return [];
    return [...new Set(catalogue.filter(c => c.sku === sku && c.quantityAvailable > 0).map(c => c.warehouseId))].sort();
  }

  function updateRow(key: string, patch: Partial<RowDraft>) {
    setRows((prev) => prev.map((r) => {
      if (r.key !== key) return r;
      const updated = { ...r, ...patch };
      // If SKU changed and current warehouse is not in new options, clear it
      if (patch.sku !== undefined && updated.sku !== r.sku) {
        const newWarehouses = getWarehouseOptionsForSku(updated.sku);
        if (!newWarehouses.includes(updated.warehouseId)) {
          updated.warehouseId = '';
        }
      }
      return updated;
    }));
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

    createM.mutate(
      { body: { items: cleanItems }, idempotencyKey: idemKey },
      {
        onSuccess: (resp) => {
          if (resp.success && resp.orderNumber) {
            notify('success', 'Sipariş oluşturuldu');
            nav(`/orders/${encodeURIComponent(resp.orderNumber)}`);
          }
        },
      },
    );
  }

  const err = createM.error ? describeError(createM.error) : null;

  if (catalogueQ.isLoading) return <LoadingState label="Loading inventory catalogue…" />;

  return (
    <div className="page-stack">
      <Card
        title="New order"
        subtitle="All-or-nothing multi-SKU reservation. Idempotency-Key is generated per request — retry safely."
      >
        {validationError && <ErrorBanner message={validationError} variant="warning" />}
        {err && <ErrorBanner message={errorCodeToUserMessage(err.code, err.status, err.message)} code={err.code} />}
        {createM.data && !createM.data.success && createM.data.failures.length > 0 && (
          <Card title="Partial failures">
            <ul className="failure-list">
              {createM.data.failures.map((f, i) => (
                <li key={i}>
                  <code>{f.sku}</code> @ <code>{f.warehouseId}</code> — {f.reason}
                </li>
              ))}
            </ul>
          </Card>
        )}

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
                  <select
                    value={r.sku}
                    onChange={(e) => updateRow(r.key, { sku: e.target.value })}
                  >
                    <option value="">-- SKU --</option>
                    {skuOptions.map(s => <option key={s} value={s}>{s}</option>)}
                  </select>
                </td>
                <td>
                  <select
                    value={r.warehouseId}
                    onChange={(e) => updateRow(r.key, { warehouseId: e.target.value })}
                    disabled={!r.sku}
                  >
                    <option value="">{r.sku ? '-- Select warehouse --' : 'Select SKU first'}</option>
                    {getWarehouseOptionsForSku(r.sku).map(w => <option key={w} value={w}>{w}</option>)}
                  </select>
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

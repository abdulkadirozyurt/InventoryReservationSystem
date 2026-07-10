import { FormEvent, useState } from 'react';

import Card from '../components/Card';
import ErrorBanner from '../components/ErrorBanner';
import LoadingState from '../components/LoadingState';
import { useInventoryCatalogue, useTransferStock } from '../hooks/useInventory';
import { describeError } from '../hooks/useOrders';
import { errorCodeToUserMessage } from '../utils/errorMessages';
import { useToast } from '../hooks/useToast';

export default function StockTransfersPage() {
  const catalogueQ = useInventoryCatalogue();
  const catalogue = catalogueQ.data;
  const skuOptions = [...new Set((catalogue ?? []).map(c => c.sku))].sort();

  const getSourceWarehouses = (selectedSku: string) => {
    if (!selectedSku || !catalogue) return [];
    return [...new Set(catalogue.filter(c => c.sku === selectedSku && c.quantityAvailable > 0).map(c => c.warehouseId))].sort();
  };

  const getTargetWarehouses = (selectedSku: string) => {
    if (!selectedSku || !catalogue) return [];
    return [...new Set(catalogue.filter(c => c.sku === selectedSku).map(c => c.warehouseId))].sort();
  };

  const [sku, setSku] = useState('');
  const [sourceWarehouseId, setSourceWarehouseId] = useState('');
  const [targetWarehouseId, setTargetWarehouseId] = useState('');
  const [quantity, setQuantity] = useState(1);
  const [reason, setReason] = useState('Warehouse rebalance');
  const [validation, setValidation] = useState<string | null>(null);
  const transferM = useTransferStock();
  const err = transferM.error ? describeError(transferM.error) : null;
  const { notify } = useToast();

  function submit(e: FormEvent) {
    e.preventDefault();
    setValidation(null);
    if (!sku) return setValidation('Select a SKU.');
    if (!sourceWarehouseId) return setValidation('Select a source warehouse.');
    if (!targetWarehouseId) return setValidation('Select a target warehouse.');
    if (sourceWarehouseId === targetWarehouseId)
      return setValidation('Source and target warehouse must differ.');
    if (quantity < 1) return setValidation('Quantity must be at least 1.');
    transferM.mutate(
      { sku, sourceWarehouseId, targetWarehouseId, quantity, reason: reason || 'Warehouse rebalance' },
      {
        onSuccess: (resp) => {
          if (resp.success) notify('success', 'Stok transferi tamamlandı');
        },
      },
    );
  }

  if (catalogueQ.isLoading) return <LoadingState label="Loading inventory catalogue…" />;

  return (
    <div className="page">
      <h1>Stock Transfers</h1>

      <Card title="Transfer stock between warehouses">
        {validation && <ErrorBanner message={validation} variant="warning" />}
        {err && <ErrorBanner message={errorCodeToUserMessage(err.code, err.status, err.message)} code={err.code} />}

        <form className="form-grid" onSubmit={submit}>
          <label>
            SKU
            <select value={sku} onChange={e => setSku(e.target.value)}>
              <option value="">-- Select SKU --</option>
              {skuOptions.map(s => <option key={s} value={s}>{s}</option>)}
            </select>
          </label>
          <label>
            Source warehouse
            <select value={sourceWarehouseId} onChange={e => setSourceWarehouseId(e.target.value)} disabled={!sku}>
              <option value="">{sku ? '-- Select source --' : 'Select SKU first'}</option>
              {getSourceWarehouses(sku).map(w => <option key={w} value={w}>{w}</option>)}
            </select>
          </label>
          <label>
            Target warehouse
            <select value={targetWarehouseId} onChange={e => setTargetWarehouseId(e.target.value)} disabled={!sku}>
              <option value="">{sku ? '-- Select target --' : 'Select SKU first'}</option>
              {getTargetWarehouses(sku).filter(w => w !== sourceWarehouseId).map(w => <option key={w} value={w}>{w}</option>)}
            </select>
          </label>
          <label>
            Quantity
            <input type="number" min={1} value={quantity} onChange={e => setQuantity(Number(e.target.value))} />
          </label>
          <label>
            Reason
            <input type="text" value={reason} onChange={e => setReason(e.target.value)} />
          </label>
          <button type="submit" className="btn btn--primary" disabled={transferM.isPending}>
            {transferM.isPending ? 'Aktarılıyor…' : 'Transfer stock'}
          </button>
        </form>

        {transferM.data && (
          <div className="result-panel">
            <strong>{transferM.data.success ? 'Transfer completed' : 'Transfer failed'}</strong>
            {transferM.data.errorMessage
              ? <span>{errorCodeToUserMessage(transferM.data.errorCode ?? 'Error', 0, transferM.data.errorMessage)}</span>
              : <span>InventoryService accepted the rebalance request.</span>}
          </div>
        )}
      </Card>
    </div>
  );
}

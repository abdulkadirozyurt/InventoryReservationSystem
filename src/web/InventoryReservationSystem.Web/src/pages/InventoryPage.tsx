import { FormEvent, useState } from "react";

import Card from "../components/Card";
import ErrorBanner from "../components/ErrorBanner";
import LoadingState from "../components/LoadingState";
import StatTile from "../components/StatTile";
import { describeError } from "../hooks/useOrders";
import { errorCodeToUserMessage } from "../utils/errorMessages";
import {
  useDecreaseStock,
  useIncreaseStock,
  useInventoryCatalogue,
  useStockLookup,
} from "../hooks/useInventory";
import { useToast } from "../hooks/useToast";

type AdjustmentMode = "increase" | "decrease";

export default function InventoryPage() {
  const catalogueQ = useInventoryCatalogue();
  const catalogue = catalogueQ.data;
  const skuOptions = [...new Set((catalogue ?? []).map((c) => c.sku))].sort();

  const getWarehouseOptionsForSku = (selectedSku: string) => {
    if (!selectedSku || !catalogue) return [];
    return [...new Set(catalogue.filter(c => c.sku === selectedSku).map(c => c.warehouseId))].sort();
  };

  const [tableFilter, setTableFilter] = useState("");
  const [sku, setSku] = useState("");
  const [warehouseId, setWarehouseId] = useState("");
  const [lookup, setLookup] = useState<{
    sku: string;
    warehouseId?: string;
  } | null>(null);
  const [mode, setMode] = useState<AdjustmentMode>("increase");
  const [adjQty, setAdjQty] = useState(1);
  const [adjReason, setAdjReason] = useState("Manual adjustment");
  const [validation, setValidation] = useState<string | null>(null);

  const stockQ = useStockLookup(
    lookup?.sku ?? "",
    lookup?.warehouseId,
    !!lookup
  );
  const stock = stockQ.data;

  const { notify } = useToast();
  const increaseM = useIncreaseStock();
  const decreaseM = useDecreaseStock();
  const activeMutation = mode === "increase" ? increaseM : decreaseM;
  const mutationError = activeMutation.error
    ? describeError(activeMutation.error)
    : null;

  function doLookup(e: FormEvent) {
    e.preventDefault();
    setValidation(null);
    if (!sku) return setValidation("Select a SKU.");
    setLookup({ sku, warehouseId: warehouseId || undefined });
  }

  function doAdjust() {
    setValidation(null);
    if (!sku) return setValidation("Select a SKU.");
    if (!warehouseId) return setValidation("Select a warehouse.");
    if (adjQty < 1) return setValidation("Quantity must be at least 1.");
    activeMutation.mutate({
      sku,
      warehouseId,
      quantity: adjQty,
      reason: adjReason || "Manual adjustment",
    }, {
      onSuccess: (resp) => {
        if (resp.success) notify('success', 'Stok güncellendi');
      },
    });
  }

  if (catalogueQ.isLoading)
    return <LoadingState label="Loading inventory catalogue…" />;

  const filteredCatalogue = (catalogue ?? []).filter((c) => {
    if (!tableFilter.trim()) return true;
    const needle = tableFilter.trim().toLowerCase();
    return c.sku.toLowerCase().includes(needle) || c.warehouseId.toLowerCase().includes(needle);
  }).sort((a, b) => a.sku.localeCompare(b.sku) || a.warehouseId.localeCompare(b.warehouseId));

  return (
    <div className="page">
      <h1>Inventory</h1>

      {validation && <ErrorBanner message={validation} variant="warning" />}
      {stockQ.error && (() => {
        const { code, message, status } = describeError(stockQ.error);
        return <ErrorBanner message={errorCodeToUserMessage(code, status, message)} code={code} />;
      })()}
      {mutationError && (
        <ErrorBanner message={errorCodeToUserMessage(mutationError.code, mutationError.status, mutationError.message)} code={mutationError.code} />
      )}

      <Card title="All stock" subtitle={`${filteredCatalogue.length} of ${catalogue?.length ?? 0} SKU/warehouse records`}>
        <div className="form-grid" style={{ marginBottom: '0.75rem' }}>
          <label style={{ flex: 1, minWidth: 220 }}>
            Filter by SKU or warehouse
            <input
              type="text"
              value={tableFilter}
              onChange={(e) => setTableFilter(e.target.value)}
              placeholder="e.g. SKU-001 or WH-1"
            />
          </label>
        </div>

        {filteredCatalogue.length === 0 ? (
          <p className="empty">
            {catalogue && catalogue.length > 0 ? 'Filtreye uyan stok kaydı yok' : 'Henüz stok kaydı yok'}
          </p>
        ) : (
          <table className="data-table">
            <thead>
              <tr>
                <th>SKU</th>
                <th>Warehouse</th>
                <th>Available</th>
                <th>Reserved</th>
                <th>Total</th>
              </tr>
            </thead>
            <tbody>
              {filteredCatalogue.map((c) => (
                <tr key={`${c.sku}-${c.warehouseId}`}>
                  <td><code>{c.sku}</code></td>
                  <td>{c.warehouseId}</td>
                  <td>{c.quantityAvailable}</td>
                  <td>{c.quantityReserved}</td>
                  <td>{c.quantityAvailable + c.quantityReserved}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Card>

      <Card title="Stock lookup">
        <form className="form-grid" onSubmit={doLookup}>
          <label>
            SKU
            <select value={sku} onChange={(e) => setSku(e.target.value)}>
              <option value="">-- Select SKU --</option>
              {skuOptions.map((s) => (
                <option key={s} value={s}>
                  {s}
                </option>
              ))}
            </select>
          </label>
          <label>
            Warehouse
            <select
              value={warehouseId}
              onChange={(e) => setWarehouseId(e.target.value)}
              disabled={!sku}
            >
              <option value=""> {sku ? 'All warehouses' : 'Select SKU first'}</option>
              {getWarehouseOptionsForSku(sku).map((w) => (
                <option key={w} value={w}>
                  {w}
                </option>
              ))}
            </select>
          </label>
          <button type="submit" className="btn btn--primary">
            Lookup
          </button>
        </form>

        {stockQ.isLoading && <LoadingState label="Querying stock…" />}

        {stock && stock.found && (
          <div className="metric-row" style={{ marginTop: "1rem" }}>
            <StatTile label="Available" value={stock.quantityAvailable} />
            <StatTile label="Reserved" value={stock.quantityReserved} />
          </div>
        )}
        {stock && !stock.found && !stock.errorMessage && (
          <p>Bu SKU-depo kombinasyonu için stok kaydı yok</p>
        )}
        {stock?.errorMessage && (
          <ErrorBanner
            message={errorCodeToUserMessage(stock.errorCode ?? 'Error', 0, stock.errorMessage)}
            code={stock.errorCode ?? undefined}
            variant="warning"
          />
        )}
      </Card>

      <Card title="Stock adjustment">
        <div className="form-grid">
          <label>
            SKU
            <select value={sku} onChange={(e) => setSku(e.target.value)}>
              <option value="">-- Select SKU --</option>
              {skuOptions.map((s) => <option key={s} value={s}>{s}</option>)}
            </select>
          </label>
          <label>
            Warehouse
            <select value={warehouseId} onChange={(e) => setWarehouseId(e.target.value)} disabled={!sku}>
              <option value="">{sku ? '-- Select warehouse --' : 'Select SKU first'}</option>
              {getWarehouseOptionsForSku(sku).map((w) => <option key={w} value={w}>{w}</option>)}
            </select>
          </label>
          <label>
            Direction
            <select
              value={mode}
              onChange={(e) => setMode(e.target.value as AdjustmentMode)}
            >
              <option value="increase">Increase (stock in)</option>
              <option value="decrease">Decrease (stock out)</option>
            </select>
          </label>
          <label>
            Quantity
            <input
              type="number"
              min={1}
              value={adjQty}
              onChange={(e) => setAdjQty(Number(e.target.value))}
            />
          </label>
          <label>
            Reason
            <input
              type="text"
              value={adjReason}
              onChange={(e) => setAdjReason(e.target.value)}
            />
          </label>
          <button
            type="button"
            className="btn btn--primary"
            disabled={activeMutation.isPending}
            onClick={doAdjust}
          >
            {activeMutation.isPending ? "İşleniyor…" : "Apply adjustment"}
          </button>
        </div>

        {activeMutation.data && (
          <div className="result-panel">
            <strong>
              {activeMutation.data.success
                ? "Adjustment saved"
                : "Adjustment failed"}
            </strong>
            {activeMutation.data.errorMessage && (
              <span>
                {errorCodeToUserMessage(activeMutation.data.errorCode ?? 'Error', 0, activeMutation.data.errorMessage)}
              </span>
            )}
          </div>
        )}
      </Card>
    </div>
  );
}

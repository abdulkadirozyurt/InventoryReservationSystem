import { FormEvent, useState } from "react";

import Card from "../components/Card";
import ErrorBanner from "../components/ErrorBanner";
import LoadingState from "../components/LoadingState";
import StatTile from "../components/StatTile";
import { describeError } from "../hooks/useOrders";
import {
  useDecreaseStock,
  useIncreaseStock,
  useInventoryCatalogue,
  useStockLookup,
} from "../hooks/useInventory";

type AdjustmentMode = "increase" | "decrease";

export default function InventoryPage() {
  const catalogueQ = useInventoryCatalogue();
  const catalogue = catalogueQ.data;
  const skuOptions = [...new Set((catalogue ?? []).map((c) => c.sku))].sort();
  const warehouseOptions = [
    ...new Set((catalogue ?? []).map((c) => c.warehouseId)),
  ].sort();

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
    });
  }

  if (catalogueQ.isLoading)
    return <LoadingState label="Loading inventory catalogue…" />;

  return (
    <div className="page">
      <h1>Inventory</h1>

      {validation && <ErrorBanner message={validation} variant="warning" />}
      {stockQ.error && (
        <ErrorBanner
          message={describeError(stockQ.error).message}
          code={describeError(stockQ.error).code}
        />
      )}
      {mutationError && (
        <ErrorBanner message={mutationError.message} code={mutationError.code} />
      )}

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
            >
              <option value="">All warehouses</option>
              {warehouseOptions.map((w) => (
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
          <p>No stock record found for that SKU / warehouse.</p>
        )}
        {stock?.errorMessage && (
          <ErrorBanner
            message={stock.errorMessage}
            code={stock.errorCode ?? undefined}
            variant="warning"
          />
        )}
      </Card>

      <Card title="Stock adjustment">
        <div className="form-grid">
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
            {activeMutation.isPending ? "Saving…" : "Apply adjustment"}
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
                {activeMutation.data.errorCode}:{" "}
                {activeMutation.data.errorMessage}
              </span>
            )}
          </div>
        )}
      </Card>
    </div>
  );
}

export interface GetStockResponse {
  sku: string;
  warehouseId: string | null;
  quantityAvailable: number;
  quantityReserved: number;
  found: boolean;
  errorCode: string | null;
  errorMessage: string | null;
}

export interface StockAdjustmentRequest {
  sku: string;
  warehouseId: string;
  quantity: number;
  reason: string;
}

export interface StockAdjustmentResponse {
  success: boolean;
  sku: string;
  warehouseId: string;
  quantityAvailable: number;
  quantityReserved: number;
  errorCode: string | null;
  errorMessage: string | null;
}

export interface TransferStockRequest {
  sku: string;
  sourceWarehouseId: string;
  targetWarehouseId: string;
  quantity: number;
  reason: string;
}

export interface InventoryOperationResponse {
  success: boolean;
  errorCode: string | null;
  errorMessage: string | null;
}

export interface CreateSnapshotRequest {
  requestedBy: string;
}

export interface CreateSnapshotResponse {
  success: boolean;
  snapshotId: string | null;
  errorCode: string | null;
  errorMessage: string | null;
}

export interface RestoreSnapshotRequest {
  requestedBy: string;
}

export interface CatalogueItem {
  sku: string;
  warehouseId: string;
  quantityAvailable: number;
  quantityReserved: number;
}

export interface InventorySearchQuery {
  search?: string;
  sku?: string;
  warehouseId?: string;
}

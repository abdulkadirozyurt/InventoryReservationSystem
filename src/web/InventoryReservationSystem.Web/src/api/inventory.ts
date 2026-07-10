import { request } from './http';
import type {
  CreateSnapshotRequest,
  CreateSnapshotResponse,
  CatalogueItem,
  GetStockResponse,
  InventoryOperationResponse,
  InventorySearchQuery,
  RestoreSnapshotRequest,
  StockAdjustmentRequest,
  StockAdjustmentResponse,
  TransferStockRequest,
} from '../types/inventory';

export const inventoryApi = {
  search(query?: InventorySearchQuery, signal?: AbortSignal): Promise<CatalogueItem[]> {
    const params = new URLSearchParams();
    if (query?.search) params.set('search', query.search);
    if (query?.sku) params.set('sku', query.sku);
    if (query?.warehouseId) params.set('warehouseId', query.warehouseId);
    const qs = params.toString();
    return request<CatalogueItem[]>(`/inventory/items${qs ? `?${qs}` : ''}`, { signal });
  },

  stock(sku: string, warehouseId?: string, signal?: AbortSignal): Promise<GetStockResponse> {
    return request<GetStockResponse>('/inventory/stock', {
      method: 'GET',
      query: { sku, warehouseId },
      signal,
    });
  },

  increase(body: StockAdjustmentRequest, signal?: AbortSignal): Promise<StockAdjustmentResponse> {
    return request<StockAdjustmentResponse>('/inventory/stock/increase', {
      method: 'POST',
      body,
      signal,
    });
  },

  decrease(body: StockAdjustmentRequest, signal?: AbortSignal): Promise<StockAdjustmentResponse> {
    return request<StockAdjustmentResponse>('/inventory/stock/decrease', {
      method: 'POST',
      body,
      signal,
    });
  },

  transfer(body: TransferStockRequest, signal?: AbortSignal): Promise<InventoryOperationResponse> {
    return request<InventoryOperationResponse>('/inventory/transfers', {
      method: 'POST',
      body,
      signal,
    });
  },

  createSnapshot(body: CreateSnapshotRequest, signal?: AbortSignal): Promise<CreateSnapshotResponse> {
    return request<CreateSnapshotResponse>('/inventory/snapshots', {
      method: 'POST',
      body,
      signal,
    });
  },

  restoreSnapshot(
    snapshotId: string,
    body: RestoreSnapshotRequest,
    signal?: AbortSignal,
  ): Promise<InventoryOperationResponse> {
    return request<InventoryOperationResponse>(`/inventory/snapshots/${encodeURIComponent(snapshotId)}/restore`, {
      method: 'POST',
      body,
      signal,
    });
  },
};

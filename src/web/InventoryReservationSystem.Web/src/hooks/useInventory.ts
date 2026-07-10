import { useMutation, useQuery } from '@tanstack/react-query';

import { inventoryApi } from '../api/inventory';
import type {
  CreateSnapshotRequest,
  InventorySearchQuery,
  RestoreSnapshotRequest,
  StockAdjustmentRequest,
  TransferStockRequest,
} from '../types/inventory';

export const inventoryKeys = {
  all: ['inventory'] as const,
  catalogue: (q?: InventorySearchQuery) => [...inventoryKeys.all, 'catalogue', q ?? {}] as const,
  stock: (sku: string, warehouseId?: string) => [...inventoryKeys.all, 'stock', sku, warehouseId ?? 'all'] as const,
};

export function useInventoryCatalogue(query?: InventorySearchQuery) {
  return useQuery({
    queryKey: inventoryKeys.catalogue(query),
    queryFn: ({ signal }) => inventoryApi.search(query, signal),
    staleTime: 30_000,
  });
}

export function useStockLookup(sku: string, warehouseId?: string, enabled = false) {
  return useQuery({
    queryKey: inventoryKeys.stock(sku, warehouseId),
    queryFn: ({ signal }) => inventoryApi.stock(sku, warehouseId, signal),
    enabled: enabled && sku.trim().length > 0,
  });
}

export function useIncreaseStock() {
  return useMutation({
    mutationFn: (body: StockAdjustmentRequest) => inventoryApi.increase(body),
  });
}

export function useDecreaseStock() {
  return useMutation({
    mutationFn: (body: StockAdjustmentRequest) => inventoryApi.decrease(body),
  });
}

export function useTransferStock() {
  return useMutation({
    mutationFn: (body: TransferStockRequest) => inventoryApi.transfer(body),
  });
}

export function useCreateSnapshot() {
  return useMutation({
    mutationFn: (body: CreateSnapshotRequest) => inventoryApi.createSnapshot(body),
  });
}

export function useRestoreSnapshot() {
  return useMutation({
    mutationFn: (payload: { snapshotId: string; body: RestoreSnapshotRequest }) =>
      inventoryApi.restoreSnapshot(payload.snapshotId, payload.body),
  });
}

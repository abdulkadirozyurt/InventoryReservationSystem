import {
  useMutation,
  useQuery,
  useQueryClient,
  type UseMutationResult,
} from '@tanstack/react-query';

import { ordersApi } from '../api/orders';
import type {
  BulkCancelOrdersRequest,
  BulkCancelOrdersResponse,
  CancelOrderResponse,
  ConfirmOrderResponse,
  CreateOrderRequest,
  CreateOrderResponse,
  ListOrdersQuery,
} from '../types/orders';

export const orderKeys = {
  all: ['orders'] as const,
  list: (q: ListOrdersQuery) => ['orders', 'list', q] as const,
  detail: (orderNumber: string) => ['orders', 'detail', orderNumber] as const,
  analytics: (from: string, to: string) => ['orders', 'analytics', from, to] as const,
};

export function useOrderList(query: ListOrdersQuery) {
  return useQuery({
    queryKey: orderKeys.list(query),
    queryFn: ({ signal }) => ordersApi.list(query, signal),
  });
}

export function useOrder(orderNumber: string | undefined) {
  return useQuery({
    queryKey: orderKeys.detail(orderNumber ?? ''),
    queryFn: ({ signal }) => ordersApi.get(orderNumber as string, signal),
    enabled: Boolean(orderNumber),
  });
}

export function useOrderAnalytics(from: string, to: string) {
  return useQuery({
    queryKey: orderKeys.analytics(from, to),
    queryFn: ({ signal }) => ordersApi.analytics(from, to, signal),
    enabled: Boolean(from) && Boolean(to),
    staleTime: 60_000,
  });
}

export function useCreateOrder(): UseMutationResult<
  CreateOrderResponse,
  Error,
  { body: CreateOrderRequest; idempotencyKey: string }
> {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ body, idempotencyKey }) => ordersApi.create(body, idempotencyKey),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: orderKeys.all });
    },
  });
}

export function useConfirmOrder(orderNumber: string): UseMutationResult<
  ConfirmOrderResponse,
  Error,
  void
> {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: () => ordersApi.confirm(orderNumber),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: orderKeys.detail(orderNumber) });
      void qc.invalidateQueries({ queryKey: orderKeys.all });
    },
  });
}

export function useCancelOrder(orderNumber: string): UseMutationResult<
  CancelOrderResponse,
  Error,
  { reason?: string }
> {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ reason }) => ordersApi.cancel(orderNumber, { reason }),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: orderKeys.detail(orderNumber) });
      void qc.invalidateQueries({ queryKey: orderKeys.all });
    },
  });
}

export function useBulkCancel(): UseMutationResult<
  BulkCancelOrdersResponse,
  Error,
  BulkCancelOrdersRequest
> {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body) => ordersApi.bulkCancel(body),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: orderKeys.all });
    },
  });
}

/** Handy tuple for routing failures into shared ErrorBanner. */
export type AnyOrderError = Error;

export function describeError(err: unknown): { code: string; message: string; status: number } {
  if (err instanceof Error) {
    const code = 'code' in err ? String((err as { code: unknown }).code) : 'Error';
    const status = 'status' in err ? Number((err as { status: unknown }).status) || 0 : 0;
    return { code, message: err.message, status };
  }
  return { code: 'Error', message: 'Unknown error', status: 0 };
}

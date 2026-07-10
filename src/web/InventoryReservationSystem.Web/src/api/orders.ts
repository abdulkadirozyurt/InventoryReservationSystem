import { request } from './http';
import type {
  BulkCancelOrdersRequest,
  BulkCancelOrdersResponse,
  CancelOrderRequest,
  CancelOrderResponse,
  ConfirmOrderResponse,
  CreateOrderRequest,
  CreateOrderResponse,
  ListOrdersQuery,
  ListOrdersResponse,
  Order,
  OrderAnalytics,
} from '../types/orders';

/**
 * Typed OrderService REST client.
 * Endpoints mirror src/services/OrderService/OrderService.API/endpoints/OrderEndpoints.cs
 * Routes: /api/orders/{list, get, create, cancel, confirm, bulk-cancel, analytics}
 */
export const ordersApi = {
  async list(query: ListOrdersQuery = {}, signal?: AbortSignal): Promise<Order[]> {
    const response = await request<ListOrdersResponse>('/orders', {
      method: 'GET',
      query: {
        status: query.status,
        from: query.from,
        to: query.to,
      },
      signal,
    });

    return response.orders;
  },

  get(orderNumber: string, signal?: AbortSignal): Promise<Order> {
    return request<Order>(`/orders/${encodeURIComponent(orderNumber)}`, {
      method: 'GET',
      signal,
    });
  },

  create(
    body: CreateOrderRequest,
    idempotencyKey: string,
    signal?: AbortSignal,
  ): Promise<CreateOrderResponse> {
    return request<CreateOrderResponse>('/orders', {
      method: 'POST',
      body,
      signal,
      headers: { 'Idempotency-Key': idempotencyKey },
    });
  },

  confirm(orderNumber: string, signal?: AbortSignal): Promise<ConfirmOrderResponse> {
    return request<ConfirmOrderResponse>(
      `/orders/${encodeURIComponent(orderNumber)}/confirm`,
      { method: 'POST', signal },
    );
  },

  cancel(
    orderNumber: string,
    payload: CancelOrderRequest = {},
    signal?: AbortSignal,
  ): Promise<CancelOrderResponse> {
    return request<CancelOrderResponse>(
      `/orders/${encodeURIComponent(orderNumber)}/cancel`,
      { method: 'POST', body: payload, signal },
    );
  },

  bulkCancel(
    body: BulkCancelOrdersRequest,
    signal?: AbortSignal,
  ): Promise<BulkCancelOrdersResponse> {
    return request<BulkCancelOrdersResponse>('/orders/bulk-cancel', {
      method: 'POST',
      body,
      signal,
    });
  },

  /**
   * Analytics requires from + to (ISO strings) and caps at 31 days.
   */
  analytics(
    from: string,
    to: string,
    signal?: AbortSignal,
  ): Promise<OrderAnalytics> {
    return request<OrderAnalytics>('/orders/analytics', {
      method: 'GET',
      query: { from, to },
      signal,
    });
  },
};

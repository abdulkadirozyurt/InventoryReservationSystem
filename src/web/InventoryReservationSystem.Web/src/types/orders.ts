/**
 * Shared DTO types mirroring OrderService REST contract.
 * Keep these aligned with OrderService.API.Endpoints.OrderEndpoints.cs
 * record declarations (CreateOrderRequest, BulkCancelOrdersRequest, etc).
 */

export type OrderStatus = 'Pending' | 'Confirmed' | 'Cancelled' | 'Expired';

export interface CreateOrderItemRequest {
  sku: string;
  warehouseId: string;
  quantity: number;
}

export interface CreateOrderRequest {
  items: CreateOrderItemRequest[];
}

export interface CreateOrderFailure {
  sku: string;
  warehouseId: string;
  errorCode: string;
  reason: string;
}

export interface CreateOrderResponse {
  success: boolean;
  orderNumber: string;
  reservationId: string;
  failures: CreateOrderFailure[];
}

export interface OrderItem {
  sku: string;
  warehouseId: string;
  requestedQuantity: number;
  reservedQuantity: number;
}

export interface Order {
  orderNumber: string;
  status: OrderStatus;
  reservationId: string | null;
  items: OrderItem[];
  createdAt: string;
  updatedAt: string;
}

export interface ListOrdersResponse {
  orders: Order[];
}

export interface CancelOrderRequest {
  reason?: string;
}

export interface CancelOrderResponse {
  orderNumber: string;
  success: boolean;
  errorCode: string | null;
  errorMessage: string | null;
}

export interface BulkCancelOrdersRequest {
  orderNumbers: string[];
  reason?: string;
}

export interface BulkCancelOrdersResponse {
  results: CancelOrderResponse[];
}

export interface ConfirmOrderResponse {
  orderNumber: string;
  success: boolean;
  errorCode: string | null;
  errorMessage: string | null;
}

export interface OrderAnalytics {
  reservationDensity: number;
  successRatio: number;
  failureRatio: number;
  averageFulfillmentDurationSeconds: number;
  totalOrdersFound: number;
}

export interface ListOrdersQuery {
  status?: OrderStatus;
  from?: string;
  to?: string;
}

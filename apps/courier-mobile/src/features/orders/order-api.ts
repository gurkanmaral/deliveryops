import { coreApi } from '@/shared/api';
import type { AvailableOrder, CourierShiftSummary, Order, PagedResponse } from '@/shared/types';

export const orderKeys = {
  all: ['orders'] as const,
  available: ['orders', 'available'] as const,
  shiftSummary: ['orders', 'shift-summary'] as const,
};

export const getShiftSummary = () => coreApi<CourierShiftSummary>('/api/v1/couriers/me/shift-summary');

export const getOrders = () => coreApi<PagedResponse<Order>>('/api/v1/orders?page=1&pageSize=100&sort=-created');
export const getAvailableOrders = () => coreApi<PagedResponse<AvailableOrder>>('/api/v1/orders/available?page=1&pageSize=100');
export const claimOrder = (id: string) => coreApi<Order>(`/api/v1/orders/${id}/claim`, { method: 'POST' });
export const changeOrderStatus = (id: string, status: number) => coreApi<Order>(`/api/v1/orders/${id}/status`, {
  method: 'PATCH',
  body: JSON.stringify({ status }),
});
export const reportDeliveryFailure = (id: string, reason: string) => coreApi<Order>(`/api/v1/orders/${id}/delivery-failure`, {
  method: 'POST',
  body: JSON.stringify({ reason }),
});

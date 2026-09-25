import { CourierAvailability, DeliveryStatus, OrderSource, OrderStatus } from './types';

export const orderStatusLabels: Record<OrderStatus, string> = {
  [OrderStatus.New]: 'Yeni',
  [OrderStatus.Confirmed]: 'Onaylandı',
  [OrderStatus.WaitingForCourier]: 'Kurye bekliyor',
  [OrderStatus.Assigned]: 'Atandı',
  [OrderStatus.PickedUp]: 'Teslim alındı',
  [OrderStatus.OnTheWay]: 'Yolda',
  [OrderStatus.Delivered]: 'Teslim edildi',
  [OrderStatus.Cancelled]: 'İptal',
  [OrderStatus.DeliveryFailed]: 'Teslim edilemedi',
  [OrderStatus.Returned]: 'İade edildi',
};

export const orderSourceLabels: Record<OrderSource, string> = {
  [OrderSource.Phone]: 'Telefon',
  [OrderSource.AdminPanel]: 'Yönetim paneli',
  [OrderSource.BusinessPanel]: 'İşletme paneli',
  [OrderSource.Yemeksepeti]: 'Yemeksepeti',
  [OrderSource.Getir]: 'Getir',
  [OrderSource.Pos]: 'POS',
  [OrderSource.Other]: 'Diğer',
  [OrderSource.Trendyol]: 'Trendyol',
};

export const availabilityLabels: Record<CourierAvailability, string> = {
  [CourierAvailability.Offline]: 'Çevrimdışı',
  [CourierAvailability.Available]: 'Aktif',
  [CourierAvailability.OnBreak]: 'Molada',
  [CourierAvailability.OffShift]: 'Mesai dışında',
};

export const deliveryStatusLabels: Record<DeliveryStatus, string> = {
  [DeliveryStatus.WaitingForAssignment]: 'Paket bekliyor',
  [DeliveryStatus.GoingToPickup]: 'Paketi almaya gidiyor',
  [DeliveryStatus.Delivering]: 'Teslimatta',
};

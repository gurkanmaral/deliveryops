export enum OrderStatus {
  New,
  Confirmed,
  WaitingForCourier,
  Assigned,
  PickedUp,
  OnTheWay,
  Delivered,
  Cancelled,
  DeliveryFailed,
  Returned,
}

export enum OrderSource {
  Phone,
  AdminPanel,
  BusinessPanel,
  Yemeksepeti,
  Getir,
  Pos,
  Other,
  Trendyol,
}

export enum CourierAvailability {
  Offline,
  Available,
  OnBreak,
  OffShift,
}

export enum DeliveryStatus {
  WaitingForAssignment,
  GoingToPickup,
  Delivering,
}

export enum DeliveryFulfillmentType {
  MerchantCourier,
  ProviderCourier,
  CustomerPickup,
}

export enum PaymentMethod {
  Unspecified,
  Online,
  Cash,
  Card,
}

export enum PaymentStatus {
  Unpaid,
  Paid,
}

export type CourierShiftSummary = {
  courierId: string;
  isOnShift: boolean;
  shiftStartedAtUtc: string | null;
  deliveredCount: number;
  activeOrderCount: number;
  cashCollected: number;
  cardCollected: number;
  collectionCount: number;
  currency: string;
};

export type CourierProfile = {
  id: string;
  businessId: string;
  branchId: string | null;
  firstName: string;
  lastName: string;
  phoneNumber: string;
  availability: CourierAvailability;
  deliveryStatus: DeliveryStatus;
  isActive: boolean;
  isShiftActive: boolean;
  shiftStartedAtUtc: string | null;
};

export type Order = {
  id: string;
  businessId: string;
  branchId: string;
  courierId: string | null;
  externalId: string;
  customerName: string;
  customerPhone: string;
  deliveryAddress: string;
  deliveryLatitude: number | null;
  deliveryLongitude: number | null;
  deliveryInstructions: string | null;
  deliveryFulfillment: DeliveryFulfillmentType;
  source: OrderSource;
  status: OrderStatus;
  totalAmount: number;
  currency: string;
  createdAtUtc: string;
  cancellationReason: string | null;
  deliveryFailureReason: string | null;
  allowedNextStatuses: OrderStatus[];
  paymentMethod?: PaymentMethod;
  paymentStatus?: PaymentStatus;
  paidAmount?: number | null;
};

export type AvailableOrder = {
  id: string;
  businessId: string;
  branchId: string;
  pickupName: string;
  pickupAddress: string;
  pickupDistanceKm: number;
  source: OrderSource;
  status: OrderStatus;
  totalAmount: number;
  currency: string;
  createdAtUtc: string;
  waitingMinutes: number;
};

export type PagedResponse<T> = {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
};

export type MobileAuthResponse = {
  accessToken: string;
  refreshToken: string;
  expiresAtUtc: string;
};

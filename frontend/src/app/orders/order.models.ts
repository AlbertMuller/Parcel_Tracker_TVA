export const ORDER_STATUSES = ['Pending', 'Confirmed', 'Fulfilled', 'Cancelled'] as const;
export type OrderStatus = (typeof ORDER_STATUSES)[number];

export interface Customer {
  id: string;
  email: string;
  name: string;
}

export interface LineItem {
  sku: string;
  name: string;
  quantity: number;
  unitPrice: number;
  lineTotal: number;
  /** Price as submitted, in the order's base currency. */
  baseUnitPrice: number;
}

export interface StatusChange {
  from: OrderStatus | null;
  to: OrderStatus;
  at: string;
}

export interface CurrencyChange {
  from: string;
  to: string;
  /** Units of `to` per 1 unit of the order's base currency. */
  rateFromBase: number;
  rateDate: string;
  rateSource: string;
  at: string;
}

export interface Order {
  id: string;
  number: number;
  externalReference: string;
  customer: Customer;
  currency: string;
  baseCurrency: string;
  canChangeCurrency: boolean;
  notes: string | null;
  lineItems: LineItem[];
  subtotal: number;
  total: number;
  status: OrderStatus;
  allowedTransitions: OrderStatus[];
  statusHistory: StatusChange[];
  currencyHistory: CurrencyChange[];
  createdAt: string;
  updatedAt: string;
}

export interface OrderSummary {
  id: string;
  number: number;
  externalReference: string;
  customerName: string;
  customerEmail: string;
  currency: string;
  lineCount: number;
  total: number;
  status: OrderStatus;
  createdAt: string;
  updatedAt: string;
}

export interface Page<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface CreateOrderRequest {
  externalReference: string;
  customer: { email: string; name: string };
  currency: string;
  notes: string | null;
  lineItems: { sku: string; name: string; quantity: number; unitPrice: number }[];
}

/** Result of submitting an order: either newly created, or an earlier submission of the same reference. */
export interface SubmitResult {
  order: Order;
  alreadySubmitted: boolean;
}

export interface ExchangeRates {
  base: string;
  date: string;
  source: string;
  fetchedAt: string;
  isStale: boolean;
  /** Units of each currency per 1 unit of `base`. */
  rates: Record<string, number>;
}

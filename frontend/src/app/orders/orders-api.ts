import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { CreateOrderRequest, Order, OrderStatus, OrderSummary, Page, SubmitResult } from './order.models';

export interface OrderListQuery {
  status?: OrderStatus | '';
  search?: string;
  page?: number;
  pageSize?: number;
}

@Injectable({ providedIn: 'root' })
export class OrdersApi {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/orders';

  list(query: OrderListQuery = {}): Observable<Page<OrderSummary>> {
    let params = new HttpParams();
    if (query.status) params = params.set('status', query.status);
    if (query.search?.trim()) params = params.set('search', query.search.trim());
    if (query.page) params = params.set('page', query.page);
    if (query.pageSize) params = params.set('pageSize', query.pageSize);
    return this.http.get<Page<OrderSummary>>(this.baseUrl, { params });
  }

  get(id: string): Observable<Order> {
    return this.http.get<Order>(`${this.baseUrl}/${id}`);
  }

  /** 201 = new order; 200 = this reference was already submitted with the same details. */
  submit(request: CreateOrderRequest): Observable<SubmitResult> {
    return this.http
      .post<Order>(this.baseUrl, request, { observe: 'response' })
      .pipe(map((res) => ({ order: res.body!, alreadySubmitted: res.status === 200 })));
  }

  changeStatus(id: string, status: OrderStatus): Observable<Order> {
    return this.http.patch<Order>(`${this.baseUrl}/${id}/status`, { status });
  }

  /** Re-prices a Pending order in another currency at the latest rate (server-side). */
  changeCurrency(id: string, currency: string): Observable<Order> {
    return this.http.patch<Order>(`${this.baseUrl}/${id}/currency`, { currency });
  }
}

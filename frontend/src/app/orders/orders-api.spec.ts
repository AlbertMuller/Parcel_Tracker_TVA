import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { OrdersApi } from './orders-api';
import { CreateOrderRequest, SubmitResult } from './order.models';

describe('OrdersApi', () => {
  let api: OrdersApi;
  let http: HttpTestingController;
  const request: CreateOrderRequest = {
    externalReference: 'PO-1',
    customer: { email: 'a@b.com', name: 'A' },
    currency: 'USD',
    notes: null,
    lineItems: [{ sku: 'S', name: 'N', quantity: 1, unitPrice: 1 }],
  };

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(OrdersApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('treats 201 as a newly created order', () => {
    let result: SubmitResult | undefined;
    api.submit(request).subscribe((r) => (result = r));
    http.expectOne({ method: 'POST', url: '/api/orders' }).flush({ id: '1' }, { status: 201, statusText: 'Created' });
    expect(result?.alreadySubmitted).toBe(false);
  });

  it('treats 200 as a repeat submission of an existing order', () => {
    let result: SubmitResult | undefined;
    api.submit(request).subscribe((r) => (result = r));
    http.expectOne({ method: 'POST', url: '/api/orders' }).flush({ id: '1' }, { status: 200, statusText: 'OK' });
    expect(result?.alreadySubmitted).toBe(true);
  });

  it('passes list filters as query parameters', () => {
    api.list({ status: 'Confirmed', search: ' acme ', pageSize: 200 }).subscribe();
    const req = http.expectOne((r) => r.url === '/api/orders');
    expect(req.request.params.get('status')).toBe('Confirmed');
    expect(req.request.params.get('search')).toBe('acme');
    expect(req.request.params.get('pageSize')).toBe('200');
    req.flush({ items: [], page: 1, pageSize: 200, totalCount: 0 });
  });
});

import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { signal } from '@angular/core';
import { of } from 'rxjs';
import { vi, afterEach } from 'vitest';
import { OrderList } from './order-list';
import { OrdersApi } from './orders-api';
import { CurrencyApi } from '../shared/currency-api';

// Exercise paging and filter resets without changing the running app's data.
describe('OrderList', () => {
  afterEach(() => vi.useRealTimers());

  it('requests the next page and returns to page one when filters change', async () => {
    vi.useFakeTimers();
    const list = vi.fn().mockReturnValue(of({ items: [], totalCount: 51, page: 1, pageSize: 25 }));
    TestBed.configureTestingModule({
      imports: [OrderList],
      providers: [
        provideRouter([]),
        { provide: OrdersApi, useValue: { list } },
        { provide: CurrencyApi, useValue: { currencies: signal([]), loadCurrencies: vi.fn() } },
      ],
    });
    const fixture = TestBed.createComponent(OrderList);
    const component = fixture.componentInstance;
    fixture.detectChanges();
    expect(list).toHaveBeenLastCalledWith({ status: '', search: '', page: 1, pageSize: 25 });
    component['goToPage'](2);
    await vi.advanceTimersByTimeAsync(300);
    expect(list).toHaveBeenLastCalledWith({ status: '', search: '', page: 2, pageSize: 25 });
    component['onStatusChange']('Pending');
    await vi.advanceTimersByTimeAsync(300);
    expect(list).toHaveBeenLastCalledWith({ status: 'Pending', search: '', page: 1, pageSize: 25 });
    component['onSearch']('missing');
    await vi.advanceTimersByTimeAsync(300);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('No matching orders');
    component['clearFilters']();
    await vi.advanceTimersByTimeAsync(300);
    expect(list).toHaveBeenLastCalledWith({ status: '', search: '', page: 1, pageSize: 25 });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('A fresh start for your orders');
  });
});

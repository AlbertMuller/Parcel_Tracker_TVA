import { CurrencyPipe, DatePipe } from '@angular/common';
import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { Subject, debounceTime, switchMap, tap, catchError, of, startWith } from 'rxjs';
import { CurrencyApi, convertToBase } from '../shared/currency-api';
import { CurrencySelect } from '../shared/currency-select';
import { StatusBadge } from '../shared/status-badge';
import { Problem, toProblem } from '../shared/problem';
import { ExchangeRates, ORDER_STATUSES, OrderStatus, OrderSummary } from './order.models';
import { OrdersApi } from './orders-api';

@Component({
  selector: 'app-order-list',
  imports: [RouterLink, CurrencyPipe, DatePipe, StatusBadge, CurrencySelect],
  templateUrl: './order-list.html',
})
export class OrderList implements OnInit {
  private readonly api = inject(OrdersApi);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly reload$ = new Subject<void>();
  private readonly currencyApi = inject(CurrencyApi);

  protected readonly statuses = ORDER_STATUSES;
  protected readonly status = signal<OrderStatus | ''>('');
  protected readonly search = signal('');
  protected readonly orders = signal<OrderSummary[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly page = signal(1);
  protected readonly pageSize = 25;
  protected readonly pageCount = computed(() =>
    Math.max(1, Math.ceil(this.totalCount() / this.pageSize)),
  );
  protected readonly loading = signal(true);
  protected readonly error = signal<Problem | null>(null);

  // "Show totals in": display-only conversion so orders in different currencies can be compared.
  protected readonly currencies = this.currencyApi.currencies;
  protected readonly displayCurrency = signal('');
  protected readonly displayRates = signal<ExchangeRates | null>(null);
  protected readonly ratesError = signal(false);

  /** Sum of the listed orders' totals in the display currency. */
  protected readonly convertedTotal = computed(() => {
    const rates = this.displayRates();
    if (!rates) return null;
    let sum = 0;
    let skipped = 0;
    for (const o of this.orders()) {
      const value = convertToBase(o.total, o.currency, rates);
      if (value === null) skipped++;
      else sum += value;
    }
    return { sum: Math.round(sum * 100) / 100, skipped };
  });

  ngOnInit(): void {
    this.currencyApi.loadCurrencies();
    this.reload$
      .pipe(
        debounceTime(250),
        startWith(undefined),
        tap(() => this.loading.set(true)),
        switchMap(() =>
          this.api
            .list({
              status: this.status(),
              search: this.search(),
              page: this.page(),
              pageSize: this.pageSize,
            })
            .pipe(
              catchError((err) => {
                this.error.set(toProblem(err));
                return of(null);
              }),
            ),
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((page) => {
        this.loading.set(false);
        if (!page) return;
        this.error.set(null);
        this.orders.set(page.items);
        this.totalCount.set(page.totalCount);
      });
  }

  protected onStatusChange(value: string): void {
    this.status.set(value as OrderStatus | '');
    this.page.set(1);
    this.reload$.next();
  }

  protected onSearch(value: string): void {
    this.search.set(value);
    this.page.set(1);
    this.reload$.next();
  }

  protected onDisplayCurrencyChange(code: string): void {
    this.displayCurrency.set(code);
    this.displayRates.set(null);
    this.ratesError.set(false);
    if (!code) return;
    this.currencyApi.rates(code).subscribe((rates) => {
      if (this.displayCurrency() !== code) return; // selection changed while loading
      this.displayRates.set(rates);
      this.ratesError.set(rates === null);
    });
  }

  /** An order's total in the display currency, or null if there is no rate for its currency. */
  protected converted(order: OrderSummary): number | null {
    const rates = this.displayRates();
    return rates ? convertToBase(order.total, order.currency, rates) : null;
  }

  protected refresh(): void {
    this.reload$.next();
  }

  protected clearFilters(): void {
    this.status.set('');
    this.search.set('');
    this.page.set(1);
    this.refresh();
  }

  protected goToPage(page: number): void {
    if (this.loading() || page < 1 || page > this.pageCount()) return;
    this.page.set(page);
    this.refresh();
  }

  protected open(order: OrderSummary): void {
    this.router.navigate(['/orders', order.id]);
  }
}

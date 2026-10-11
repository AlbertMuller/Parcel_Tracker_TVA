import { CurrencyPipe, DatePipe } from '@angular/common';
import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { CurrencyApi } from '../shared/currency-api';
import { CurrencySelect } from '../shared/currency-select';
import { StatusBadge } from '../shared/status-badge';
import { Problem, toProblem } from '../shared/problem';
import { Order, OrderStatus } from './order.models';
import { OrdersApi } from './orders-api';

type Notice = 'created' | 'already-submitted' | 'currency-changed';

const ACTION_LABELS: Record<OrderStatus, string> = {
  Pending: 'Move to Pending',
  Confirmed: 'Confirm order',
  Fulfilled: 'Mark as fulfilled',
  Cancelled: 'Cancel order',
};

/** What converting to the selected currency would look like, using the latest rate. */
interface ConversionPreview {
  currency: string;
  rate: number;
  date: string | null;
  source: string | null;
  isStale: boolean;
  total: number;
}

@Component({
  selector: 'app-order-detail',
  imports: [RouterLink, CurrencyPipe, DatePipe, StatusBadge, CurrencySelect],
  templateUrl: './order-detail.html',
})
export class OrderDetail implements OnInit {
  private readonly api = inject(OrdersApi);
  private readonly currencyApi = inject(CurrencyApi);

  /** Bound from the :id route parameter. */
  readonly id = input.required<string>();

  protected readonly order = signal<Order | null>(null);
  protected readonly loadError = signal<Problem | null>(null);
  protected readonly actionError = signal<Problem | null>(null);
  protected readonly currencyError = signal<Problem | null>(null);
  protected readonly busy = signal(false);
  protected readonly notice = signal<Notice | null>((history.state?.notice as Notice) ?? null);

  protected readonly currencies = this.currencyApi.currencies;
  protected readonly targetCurrency = signal('');
  protected readonly preview = signal<ConversionPreview | null>(null);
  protected readonly previewLoading = signal(false);

  /** Currencies the order can be converted to (everything except its current one). */
  protected readonly targetOptions = computed(() => {
    const current = this.order()?.currency;
    return this.currencies().filter((c) => c.code !== current);
  });

  protected readonly isConverted = computed(() => {
    const o = this.order();
    return !!o && o.currency !== o.baseCurrency;
  });

  ngOnInit(): void {
    this.currencyApi.loadCurrencies();
    this.load();
  }

  protected actionLabel(status: OrderStatus): string {
    return ACTION_LABELS[status];
  }

  protected changeStatus(target: OrderStatus): void {
    const order = this.order();
    if (!order || this.busy()) return;
    if (target === 'Cancelled' && !confirm(`Cancel order #${order.number}? This cannot be undone.`)) return;

    this.busy.set(true);
    this.actionError.set(null);
    this.notice.set(null);
    this.api.changeStatus(order.id, target).subscribe({
      next: (updated) => {
        this.order.set(updated);
        this.resetCurrencySelection();
        this.busy.set(false);
      },
      error: (err) => {
        this.actionError.set(toProblem(err));
        this.busy.set(false);
        // The order may have been changed by someone else; show its current state.
        this.load();
      },
    });
  }

  /** Shows the new total at the latest rate before the user commits to converting. */
  protected selectCurrency(code: string): void {
    const order = this.order();
    this.targetCurrency.set(code);
    this.preview.set(null);
    this.currencyError.set(null);
    if (!order || !code) return;

    // Converting back to the base currency restores the submitted prices exactly.
    if (code === order.baseCurrency) {
      this.preview.set({ currency: code, rate: 1, date: null, source: null, isStale: false, total: this.totalAt(order, 1) });
      return;
    }

    this.previewLoading.set(true);
    this.currencyApi.rates(order.baseCurrency).subscribe((rates) => {
      this.previewLoading.set(false);
      if (this.targetCurrency() !== code) return; // selection changed while loading
      const rate = rates?.rates[code];
      if (!rates || !rate) {
        this.currencyError.set({
          status: 0,
          title: 'No exchange rate',
          detail: rates
            ? `No exchange rate is available from ${order.baseCurrency} to ${code}.`
            : 'Exchange rates could not be loaded. Try again shortly.',
        });
        return;
      }
      this.preview.set({
        currency: code,
        rate,
        date: rates.date,
        source: rates.source,
        isStale: rates.isStale,
        total: this.totalAt(order, rate),
      });
    });
  }

  protected convert(): void {
    const order = this.order();
    const target = this.targetCurrency();
    if (!order || !target || this.busy()) return;

    this.busy.set(true);
    this.currencyError.set(null);
    this.notice.set(null);
    this.api.changeCurrency(order.id, target).subscribe({
      next: (updated) => {
        this.order.set(updated);
        this.resetCurrencySelection();
        this.notice.set('currency-changed');
        this.busy.set(false);
      },
      error: (err) => {
        this.currencyError.set(toProblem(err));
        this.busy.set(false);
        this.load();
      },
    });
  }

  /** Mirrors the server: each unit price is converted from the base price and rounded to cents. */
  private totalAt(order: Order, rate: number): number {
    const cents = order.lineItems.reduce(
      (sum, l) => sum + l.quantity * Math.round(l.baseUnitPrice * rate * 100),
      0,
    );
    return cents / 100;
  }

  private resetCurrencySelection(): void {
    this.targetCurrency.set('');
    this.preview.set(null);
  }

  private load(): void {
    this.api.get(this.id()).subscribe({
      next: (order) => {
        this.order.set(order);
        this.loadError.set(null);
      },
      error: (err) => this.loadError.set(toProblem(err)),
    });
  }
}

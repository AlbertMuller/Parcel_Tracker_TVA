import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, catchError, of, shareReplay } from 'rxjs';
import { ExchangeRates } from '../orders/order.models';

export interface CurrencyOption {
  code: string;
  name: string;
}

/** Used if the API can't load the live list, so the order form still works. */
const FALLBACK_CURRENCIES: CurrencyOption[] = [
  { code: 'AUD', name: 'Australian Dollar' },
  { code: 'CAD', name: 'Canadian Dollar' },
  { code: 'CHF', name: 'Swiss Franc' },
  { code: 'EUR', name: 'Euro' },
  { code: 'GBP', name: 'British Pound' },
  { code: 'JPY', name: 'Japanese Yen' },
  { code: 'USD', name: 'United States Dollar' },
  { code: 'ZAR', name: 'South African Rand' },
];

@Injectable({ providedIn: 'root' })
export class CurrencyApi {
  private readonly http = inject(HttpClient);
  private readonly ratesByBase = new Map<string, Observable<ExchangeRates | null>>();
  private loaded = false;

  /** Supported currencies, sorted by code. Starts with a fallback list until the live list arrives. */
  readonly currencies = signal<CurrencyOption[]>(FALLBACK_CURRENCIES);

  loadCurrencies(): void {
    if (this.loaded) return;
    this.loaded = true;
    this.http.get<Record<string, string>>('/api/currencies').subscribe({
      next: (map) => this.currencies.set(Object.entries(map).map(([code, name]) => ({ code, name }))),
      error: () => (this.loaded = false), // keep the fallback list; try again next time
    });
  }

  /** Latest rates for a base currency, shared between callers. Emits null if unavailable. */
  rates(base: string): Observable<ExchangeRates | null> {
    const key = base.toUpperCase();
    let rates$ = this.ratesByBase.get(key);
    if (!rates$) {
      rates$ = this.http.get<ExchangeRates>('/api/exchange-rates', { params: { base: key } }).pipe(
        catchError(() => {
          this.ratesByBase.delete(key); // allow a retry later
          return of(null);
        }),
        shareReplay({ bufferSize: 1, refCount: false }),
      );
      this.ratesByBase.set(key, rates$);
    }
    return rates$;
  }
}

/** Converts `amount` in `from` into the base of `rates`, or null if no rate exists. */
export function convertToBase(amount: number, from: string, rates: ExchangeRates): number | null {
  if (from === rates.base) return amount;
  const rate = rates.rates[from];
  return rate ? Math.round((amount / rate) * 100) / 100 : null;
}

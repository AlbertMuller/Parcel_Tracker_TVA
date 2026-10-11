import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { CurrencyApi, convertToBase } from './currency-api';
import { ExchangeRates } from '../orders/order.models';

const eurRates: ExchangeRates = {
  base: 'EUR',
  date: '2026-10-09',
  source: 'ECB',
  fetchedAt: '2026-10-09T16:00:00Z',
  isStale: false,
  rates: { USD: 1.12, GBP: 0.8 },
};

describe('convertToBase', () => {
  it('converts into the rates base by dividing by the rate', () => {
    expect(convertToBase(112, 'USD', eurRates)).toBe(100);
    expect(convertToBase(10, 'GBP', eurRates)).toBe(12.5);
  });

  it('returns the amount unchanged for the base currency', () => {
    expect(convertToBase(42.5, 'EUR', eurRates)).toBe(42.5);
  });

  it('returns null when there is no rate', () => {
    expect(convertToBase(10, 'XYZ', eurRates)).toBeNull();
  });
});

describe('CurrencyApi', () => {
  let api: CurrencyApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(CurrencyApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('shares one request per base currency', () => {
    const results: (ExchangeRates | null)[] = [];
    api.rates('eur').subscribe((r) => results.push(r));
    api.rates('EUR').subscribe((r) => results.push(r));
    http.expectOne('/api/exchange-rates?base=EUR').flush(eurRates);
    expect(results).toEqual([eurRates, eurRates]);
  });

  it('emits null and allows a retry when rates fail to load', () => {
    let result: ExchangeRates | null | undefined;
    api.rates('EUR').subscribe((r) => (result = r));
    http.expectOne('/api/exchange-rates?base=EUR').flush({}, { status: 503, statusText: 'Unavailable' });
    expect(result).toBeNull();

    api.rates('EUR').subscribe();
    http.expectOne('/api/exchange-rates?base=EUR').flush(eurRates);
  });

  it('replaces the fallback currency list with the live one', () => {
    api.loadCurrencies();
    http.expectOne('/api/currencies').flush({ EUR: 'Euro', USD: 'US Dollar' });
    expect(api.currencies().map((c) => c.code)).toEqual(['EUR', 'USD']);
  });
});

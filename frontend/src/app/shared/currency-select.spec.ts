import { beforeAll, afterAll, vi } from 'vitest';
import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { CurrencyOption } from './currency-api';
import { CurrencySelect } from './currency-select';

const OPTIONS: CurrencyOption[] = [
  { code: 'EUR', name: 'Euro' },
  { code: 'GBP', name: 'British Pound' },
  { code: 'JPY', name: 'Japanese Yen' },
  { code: 'USD', name: 'United States Dollar' },
];

@Component({
  imports: [CurrencySelect, ReactiveFormsModule],
  template: `
    <label>Currency <app-currency-select [formControl]="control" [options]="options" /></label>
    <app-currency-select
      class="standalone"
      [options]="options"
      [(value)]="picked"
      emptyLabel="Order currency"
    />
  `,
})
class Host {
  readonly options = OPTIONS;
  readonly control = new FormControl('USD', { nonNullable: true });
  readonly picked = signal('');
}

describe('CurrencySelect', () => {
  const originalScroll = HTMLElement.prototype.scrollIntoView;
  beforeAll(() => {
    // jsdom has no layout engine and does not implement this browser API.
    HTMLElement.prototype.scrollIntoView = vi.fn();
  });
  afterAll(() => {
    HTMLElement.prototype.scrollIntoView = originalScroll;
  });

  async function setup() {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const root = fixture.nativeElement as HTMLElement;
    const input = root.querySelector('app-currency-select input') as HTMLInputElement;
    const type = async (el: HTMLInputElement, text: string) => {
      el.value = text;
      el.dispatchEvent(new Event('input'));
      await fixture.whenStable();
    };
    const key = async (el: HTMLInputElement, k: string) => {
      el.dispatchEvent(new KeyboardEvent('keydown', { key: k, cancelable: true }));
      await fixture.whenStable();
    };
    const options = () =>
      Array.from(root.querySelectorAll('app-currency-select li[role=option] .mono')).map((el) =>
        el.textContent!.trim(),
      );
    return { fixture, root, input, type, key, options };
  }

  it('shows the selected currency from the form control', async () => {
    const { input } = await setup();
    expect(input.value).toBe('USD · United States Dollar');
  });

  it('filters by code or name, with code matches first', async () => {
    const { input, type, options } = await setup();
    input.dispatchEvent(new Event('focus'));

    await type(input, 'pou');
    expect(options()).toEqual(['GBP']);

    await type(input, 'yen');
    expect(options()).toEqual(['JPY']);

    await type(input, 'u');
    expect(options()[0]).toBe('USD'); // code starts with "u"
    expect(options()).toContain('EUR'); // code contains "u"
  });

  it('picks the highlighted option with Enter and updates the form control', async () => {
    const { fixture, input, type, key } = await setup();
    input.dispatchEvent(new Event('focus'));
    await type(input, 'eu');
    await key(input, 'Enter');

    expect(fixture.componentInstance.control.value).toBe('EUR');
    expect(input.value).toBe('EUR · Euro');
  });

  it('moves the highlight with the arrow keys', async () => {
    const { fixture, input, key } = await setup();
    input.dispatchEvent(new Event('focus'));
    await fixture.whenStable();
    await key(input, 'ArrowUp'); // from USD (index 3) to JPY
    await key(input, 'Enter');

    expect(fixture.componentInstance.control.value).toBe('JPY');
  });

  it('Escape closes without changing the value', async () => {
    const { fixture, input, type, key, options } = await setup();
    input.dispatchEvent(new Event('focus'));
    await type(input, 'gbp');
    await key(input, 'Escape');

    expect(options()).toEqual([]);
    expect(fixture.componentInstance.control.value).toBe('USD');
  });

  it('supports an empty option and two-way binding without a form', async () => {
    const { fixture, root, key } = await setup();
    const input = root.querySelector('app-currency-select.standalone input') as HTMLInputElement;
    expect(input.value).toBe('Order currency');

    input.dispatchEvent(new Event('focus'));
    await fixture.whenStable();
    await key(input, 'ArrowDown'); // from the empty option to EUR
    await key(input, 'Enter');
    expect(fixture.componentInstance.picked()).toBe('EUR');
  });

  it('picks an option by mouse even inside a <label>', async () => {
    const { fixture, root, input, type } = await setup();
    input.dispatchEvent(new Event('focus'));
    await type(input, 'gbp');
    (root.querySelector('app-currency-select li[role=option]') as HTMLElement).click();
    await fixture.whenStable();

    expect(fixture.componentInstance.control.value).toBe('GBP');
    expect(root.querySelector('app-currency-select [role=listbox]')).toBeNull();
    expect(input.value).toBe('GBP · British Pound');
  });
});

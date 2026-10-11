import { CurrencyPipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import {
  AbstractControl,
  FormControl,
  FormGroup,
  NonNullableFormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { CurrencyApi } from '../shared/currency-api';
import { CurrencySelect } from '../shared/currency-select';
import { Problem, toProblem } from '../shared/problem';
import { CreateOrderRequest } from './order.models';
import { OrdersApi } from './orders-api';

type LineItemForm = FormGroup<{
  sku: FormControl<string>;
  name: FormControl<string>;
  quantity: FormControl<number | null>;
  unitPrice: FormControl<number | null>;
}>;

const wholeNumber = (c: AbstractControl): ValidationErrors | null =>
  c.value == null || c.value === '' || Number.isInteger(Number(c.value)) ? null : { wholeNumber: true };

const maxTwoDecimals = (c: AbstractControl): ValidationErrors | null =>
  c.value == null || c.value === '' || Math.abs(Number(c.value) * 100 - Math.round(Number(c.value) * 100)) < 1e-6
    ? null
    : { decimals: true };

/** e.g. PO-20261006-K3QZ. Generated once per form so a retry reuses the same reference. */
function newReference(): string {
  const d = new Date();
  const date = `${d.getFullYear()}${String(d.getMonth() + 1).padStart(2, '0')}${String(d.getDate()).padStart(2, '0')}`;
  const suffix = Array.from(crypto.getRandomValues(new Uint8Array(4)), (b) => 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789'[b % 32]).join('');
  return `PO-${date}-${suffix}`;
}

const CLIENT_MESSAGES: Record<string, string> = {
  required: 'Required.',
  email: 'Enter a valid email address.',
  wholeNumber: 'Must be a whole number.',
  decimals: 'At most 2 decimal places.',
  pattern: 'Invalid format.',
  maxlength: 'Too long.',
};

@Component({
  selector: 'app-order-form',
  imports: [ReactiveFormsModule, RouterLink, CurrencyPipe, CurrencySelect],
  templateUrl: './order-form.html',
})
export class OrderForm {
  private readonly fb = inject(NonNullableFormBuilder);
  private readonly api = inject(OrdersApi);
  private readonly router = inject(Router);
  private readonly currencyApi = inject(CurrencyApi);

  protected readonly currencies = this.currencyApi.currencies;

  protected readonly form = this.fb.group({
    externalReference: [newReference(), [Validators.required, Validators.maxLength(64)]],
    customerName: ['', [Validators.required, Validators.maxLength(200)]],
    customerEmail: ['', [Validators.required, Validators.email]],
    currency: ['USD', [Validators.required, Validators.pattern(/^[A-Za-z]{3}$/)]],
    notes: ['', [Validators.maxLength(1000)]],
    lineItems: this.fb.array<LineItemForm>([this.newLine()]),
  });

  protected readonly submitting = signal(false);
  protected readonly problem = signal<Problem | null>(null);
  private readonly serverErrors = signal<Record<string, string[]>>({});

  private readonly value = toSignal(this.form.valueChanges, { initialValue: this.form.getRawValue() });

  /** Client-side preview only; the server computes the authoritative totals. */
  protected readonly estimate = computed(() => {
    const lines = (this.value().lineItems ?? []).map((l) => {
      const qty = Number(l.quantity) || 0;
      const cents = Math.round((Number(l.unitPrice) || 0) * 100);
      return (qty * cents) / 100;
    });
    return { lines, total: lines.reduce((a, b) => a + b, 0) };
  });

  protected readonly currencyCode = computed(() => {
    const code = (this.value().currency ?? '').toUpperCase();
    return /^[A-Z]{3}$/.test(code) ? code : 'USD';
  });

  constructor() {
    this.currencyApi.loadCurrencies();

    // Server-side field errors no longer apply once the user edits the form.
    this.form.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => {
      if (Object.keys(this.serverErrors()).length) this.serverErrors.set({});
    });
  }

  protected get lineItems() {
    return this.form.controls.lineItems;
  }

  protected addLine(): void {
    this.lineItems.push(this.newLine());
  }

  protected removeLine(index: number): void {
    if (this.lineItems.length > 1) this.lineItems.removeAt(index);
  }

  protected regenerateReference(): void {
    this.form.controls.externalReference.setValue(newReference());
    this.problem.set(null);
  }

  /** Messages for a field: client validation once touched, plus any server message for its JSON path. */
  protected errorsFor(control: AbstractControl, serverPath: string): string[] {
    const errors = control.touched ? control.errors : null;
    const messages = errors
      ? Object.keys(errors).map((k) =>
          k === 'min' ? (errors[k].min === 0 ? 'Cannot be negative.' : `Must be at least ${errors[k].min}.`) : (CLIENT_MESSAGES[k] ?? k),
        )
      : [];
    return [...messages, ...(this.serverErrors()[serverPath] ?? [])];
  }

  protected submit(): void {
    if (this.submitting()) return;
    this.form.markAllAsTouched();
    if (this.form.invalid) return;

    this.submitting.set(true);
    this.problem.set(null);

    this.api.submit(this.toRequest()).subscribe({
      next: ({ order, alreadySubmitted }) =>
        this.router.navigate(['/orders', order.id], {
          state: { notice: alreadySubmitted ? 'already-submitted' : 'created' },
        }),
      error: (err) => {
        const problem = toProblem(err);
        this.problem.set(problem);
        this.serverErrors.set(problem.errors ?? {});
        this.submitting.set(false);
      },
    });
  }

  private toRequest(): CreateOrderRequest {
    const v = this.form.getRawValue();
    return {
      externalReference: v.externalReference,
      customer: { email: v.customerEmail, name: v.customerName },
      currency: v.currency.toUpperCase(),
      notes: v.notes || null,
      lineItems: v.lineItems.map((l) => ({
        sku: l.sku,
        name: l.name,
        quantity: Number(l.quantity),
        unitPrice: Number(l.unitPrice),
      })),
    };
  }

  private newLine(): LineItemForm {
    return this.fb.group({
      sku: ['', [Validators.required, Validators.maxLength(64)]],
      name: ['', [Validators.required, Validators.maxLength(200)]],
      quantity: this.fb.control<number | null>(1, [Validators.required, Validators.min(1), wholeNumber]),
      unitPrice: this.fb.control<number | null>(null, [Validators.required, Validators.min(0), maxTwoDecimals]),
    });
  }
}

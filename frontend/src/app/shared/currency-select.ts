import { Component, ElementRef, computed, forwardRef, input, model, signal, viewChild } from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';
import { CurrencyOption } from './currency-api';

let nextId = 0;

/**
 * A filterable currency picker (ARIA combobox). Type a code or name to narrow the list;
 * ↑/↓ to move, Enter to pick, Escape to close.
 *
 * Works with reactive forms (formControlName) or standalone via [(value)].
 */
@Component({
  selector: 'app-currency-select',
  providers: [{ provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => CurrencySelect), multi: true }],
  host: { class: 'combo', '(focusout)': 'onFocusOut($event)' },
  template: `
    <input
      #input
      type="text"
      role="combobox"
      autocomplete="off"
      spellcheck="false"
      [id]="id"
      [attr.aria-label]="ariaLabel()"
      [attr.aria-expanded]="open()"
      [attr.aria-controls]="id + '-list'"
      [attr.aria-activedescendant]="open() && activeOption() ? optionId(activeOption()!.code) : null"
      [placeholder]="open() ? selectedLabel() || placeholder() : placeholder()"
      [value]="open() ? query() : selectedLabel()"
      [disabled]="disabled()"
      (focus)="openList()"
      (click)="openList()"
      (input)="onType($any($event.target).value)"
      (keydown)="onKeydown($event)"
    />
    @if (open()) {
      <!-- preventDefault stops a wrapping <label> from forwarding the click to the input (which would reopen the list). -->
      <ul class="combo-list" role="listbox" [id]="id + '-list'" (click)="$event.preventDefault()">
        @if (emptyLabel() && !query()) {
          <li
            role="option"
            [id]="optionId('')"
            [class.active]="activeIndex() === -1"
            [attr.aria-selected]="value() === ''"
            (mousedown)="$event.preventDefault()"
            (click)="select('')"
          >
            {{ emptyLabel() }}
          </li>
        }
        @for (c of filtered(); track c.code; let i = $index) {
          <li
            role="option"
            [id]="optionId(c.code)"
            [class.active]="i === activeIndex()"
            [attr.aria-selected]="c.code === value()"
            (mousedown)="$event.preventDefault()"
            (mouseenter)="activeIndex.set(i)"
            (click)="select(c.code)"
          >
            <span class="mono">{{ c.code }}</span> <span class="muted">{{ c.name }}</span>
          </li>
        } @empty {
          <li class="combo-empty">No currency matches "{{ query() }}"</li>
        }
      </ul>
    }
  `,
})
export class CurrencySelect implements ControlValueAccessor {
  readonly options = input.required<CurrencyOption[]>();
  readonly placeholder = input('Search currency…');
  readonly ariaLabel = input('Currency');
  /** If set, an extra option with this label selects '' (e.g. "Order currency"). */
  readonly emptyLabel = input<string | null>(null);
  readonly value = model('');

  protected readonly id = `currency-select-${nextId++}`;
  protected readonly open = signal(false);
  protected readonly query = signal('');
  /** Index into filtered(); -1 is the empty option. */
  protected readonly activeIndex = signal(0);
  protected readonly disabled = signal(false);
  private readonly inputEl = viewChild.required<ElementRef<HTMLInputElement>>('input');

  /** Codes starting with the query first, then other code/name matches. */
  protected readonly filtered = computed(() => {
    const q = this.query().trim().toLowerCase();
    const all = this.options();
    if (!q) return all;
    const codeStarts = all.filter((c) => c.code.toLowerCase().startsWith(q));
    const others = all.filter(
      (c) => !codeStarts.includes(c) && (c.code.toLowerCase().includes(q) || c.name.toLowerCase().includes(q)),
    );
    return [...codeStarts, ...others];
  });

  protected readonly activeOption = computed(() => this.filtered()[this.activeIndex()] ?? null);

  protected readonly selectedLabel = computed(() => {
    const code = this.value();
    if (!code) return this.emptyLabel() ?? '';
    const match = this.options().find((c) => c.code === code);
    return match ? `${match.code} · ${match.name}` : code;
  });

  private onChange: (value: string) => void = () => {};
  private onTouched: () => void = () => {};

  protected optionId(code: string): string {
    return `${this.id}-opt-${code || 'none'}`;
  }

  protected openList(): void {
    if (this.open() || this.disabled()) return;
    this.query.set('');
    this.open.set(true);
    const index = this.filtered().findIndex((c) => c.code === this.value());
    this.setActive(index >= 0 ? index : this.emptyLabel() && !this.value() ? -1 : 0);
  }

  protected onType(text: string): void {
    this.query.set(text);
    this.open.set(true);
    this.activeIndex.set(0);
  }

  protected onKeydown(event: KeyboardEvent): void {
    const count = this.filtered().length;
    const min = this.emptyLabel() && !this.query() ? -1 : 0;
    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault();
        if (!this.open()) this.openList();
        else this.setActive(Math.max(min, Math.min(this.activeIndex() + 1, count - 1)));
        break;
      case 'ArrowUp':
        event.preventDefault();
        this.setActive(Math.max(this.activeIndex() - 1, min));
        break;
      case 'Enter':
        if (!this.open()) return;
        event.preventDefault(); // don't submit the surrounding form
        if (this.activeIndex() === -1 && min === -1) this.select('');
        else if (this.activeOption()) this.select(this.activeOption()!.code);
        break;
      case 'Escape':
        if (this.open()) {
          event.preventDefault();
          this.close();
        }
        break;
      case 'Tab':
        this.close();
        break;
    }
  }

  protected select(code: string): void {
    this.close();
    if (code !== this.value()) {
      this.value.set(code);
      this.onChange(code);
    }
  }

  protected onFocusOut(event: FocusEvent): void {
    const host = (event.currentTarget as HTMLElement | null) ?? null;
    if (host && event.relatedTarget instanceof Node && host.contains(event.relatedTarget)) return;
    this.close();
    this.onTouched();
  }

  private close(): void {
    this.open.set(false);
    this.query.set('');
  }

  private setActive(index: number): void {
    this.activeIndex.set(index);
    const code = index === -1 ? '' : this.filtered()[index]?.code;
    if (code === undefined) return;
    // Keep the highlighted option visible while arrowing through a long list.
    queueMicrotask(() =>
      this.inputEl().nativeElement.ownerDocument.getElementById(this.optionId(code))?.scrollIntoView({ block: 'nearest' }),
    );
  }

  // ControlValueAccessor
  writeValue(value: string | null): void {
    this.value.set(value ?? '');
  }

  registerOnChange(fn: (value: string) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.disabled.set(isDisabled);
    if (isDisabled) this.close();
  }
}

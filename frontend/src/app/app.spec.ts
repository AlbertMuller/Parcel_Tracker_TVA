import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';
import { vi, afterEach } from 'vitest';

describe('App', () => {
  afterEach(() => {
    localStorage.removeItem('order-tracker-theme');
    delete document.documentElement.dataset['theme'];
    vi.restoreAllMocks();
  });

  beforeEach(async () => {
    localStorage.removeItem('order-tracker-theme');
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([])],
    }).compileComponents();
  });

  it('renders the navigation', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.brand')?.textContent).toContain('Order Tracker');
    expect(el.querySelectorAll('nav a').length).toBe(2);
  });
  it('restores a saved theme and persists a toggle', async () => {
    localStorage.setItem('order-tracker-theme', 'dark');
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const button = fixture.nativeElement.querySelector('.theme-toggle') as HTMLButtonElement;
    expect(document.documentElement.dataset['theme']).toBe('dark');
    expect(button.getAttribute('aria-pressed')).toBe('true');
    button.click();
    await fixture.whenStable();
    expect(document.documentElement.dataset['theme']).toBe('light');
    expect(button.getAttribute('aria-pressed')).toBe('false');
    expect(localStorage.getItem('order-tracker-theme')).toBe('light');
  });

  it('uses the system preference and still toggles when storage is blocked', async () => {
    Object.defineProperty(window, 'matchMedia', {
      configurable: true,
      value: vi.fn().mockReturnValue({ matches: true }),
    });
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    expect(document.documentElement.dataset['theme']).toBe('dark');
    (fixture.nativeElement.querySelector('.theme-toggle') as HTMLButtonElement).click();
    await fixture.whenStable();
    expect(document.documentElement.dataset['theme']).toBe('light');
    delete (window as Partial<Window>).matchMedia;
  });
});

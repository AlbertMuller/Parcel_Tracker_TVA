import { DOCUMENT } from '@angular/common';
import { Component, effect, inject, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './app.html',
})
export class App {
  private readonly document = inject(DOCUMENT);
  protected readonly darkMode = signal(this.initialDarkMode());

  constructor() {
    effect(() => {
      this.document.documentElement.dataset['theme'] = this.darkMode() ? 'dark' : 'light';
    });
  }

  protected toggleTheme(): void {
    this.darkMode.update((value) => !value);
    try {
      this.document.defaultView?.localStorage.setItem(
        'order-tracker-theme',
        this.darkMode() ? 'dark' : 'light',
      );
    } catch {
      // The toggle still works when browser storage is unavailable.
    }
  }

  private initialDarkMode(): boolean {
    const window = this.document.defaultView;
    try {
      const saved = window?.localStorage.getItem('order-tracker-theme');
      if (saved === 'dark' || saved === 'light') return saved === 'dark';
    } catch {
      // Use the system preference when storage is blocked.
    }
    return window?.matchMedia?.('(prefers-color-scheme: dark)').matches ?? false;
  }
}

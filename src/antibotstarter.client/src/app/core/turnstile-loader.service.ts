import { DOCUMENT } from '@angular/common';
import { Injectable, inject } from '@angular/core';

export interface TurnstileRenderOptions {
  sitekey: string;
  action?: string;
  callback: (token: string) => void;
  'expired-callback': () => void;
  'error-callback': () => void;
}

export interface TurnstileApi {
  render(container: HTMLElement, options: TurnstileRenderOptions): string;
  reset(widgetId?: string): void;
  remove(widgetId: string): void;
}

declare global {
  interface Window {
    turnstile?: TurnstileApi;
  }
}

export const TURNSTILE_SCRIPT = 'https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit';

/** Loads the Cloudflare Turnstile script only once (the server CSP allows it). */
@Injectable({ providedIn: 'root' })
export class TurnstileLoaderService {
  private readonly document = inject(DOCUMENT);
  private loading?: Promise<TurnstileApi>;

  load(): Promise<TurnstileApi> {
    const win = this.document.defaultView as Window | null;
    if (win?.turnstile) return Promise.resolve(win.turnstile);

    this.loading ??= new Promise<TurnstileApi>((resolve, reject) => {
      const script = this.document.createElement('script');
      script.src = TURNSTILE_SCRIPT;
      script.async = true;
      script.defer = true;
      script.onload = () => (win?.turnstile ? resolve(win.turnstile) : reject(new Error('Turnstile not available')));
      script.onerror = () => {
        this.loading = undefined;
        reject(new Error('Could not load Turnstile'));
      };
      this.document.head.appendChild(script);
    });
    return this.loading;
  }
}

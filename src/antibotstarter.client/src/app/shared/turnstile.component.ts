import { AfterViewInit, Component, ElementRef, OnDestroy, inject, input, output, signal, viewChild } from '@angular/core';
import { TurnstileApi, TurnstileLoaderService } from '../core/turnstile-loader.service';

/**
 * Cloudflare Turnstile widget (invisible/managed CAPTCHA).
 * Emits the token when the challenge is solved and null when it expires or fails.
 * The token is ALWAYS verified on the server.
 */
@Component({
  selector: 'app-turnstile',
  template: `
    <div #host></div>
    @if (loadError()) {
      <p class="error">The anti-bot check could not be loaded. Please reload the page.</p>
    }
  `,
})
export class TurnstileComponent implements AfterViewInit, OnDestroy {
  readonly siteKey = input.required<string>();
  readonly action = input<string>('');
  readonly token = output<string | null>();

  readonly loadError = signal(false);

  private readonly host = viewChild.required<ElementRef<HTMLElement>>('host');
  private readonly loader = inject(TurnstileLoaderService);
  private api?: TurnstileApi;
  private widgetId?: string;
  private destroyed = false;

  async ngAfterViewInit(): Promise<void> {
    try {
      const api = await this.loader.load();
      if (this.destroyed) return;
      this.api = api;
      this.widgetId = api.render(this.host().nativeElement, {
        sitekey: this.siteKey(),
        action: this.action() || undefined,
        callback: (t) => this.token.emit(t),
        'expired-callback': () => this.token.emit(null),
        'error-callback': () => this.token.emit(null),
      });
    } catch {
      this.loadError.set(true);
    }
  }

  /** Turnstile tokens are single-use: reset after every submission. */
  reset(): void {
    if (this.widgetId !== undefined) this.api?.reset(this.widgetId);
    this.token.emit(null);
  }

  ngOnDestroy(): void {
    this.destroyed = true;
    if (this.widgetId !== undefined) this.api?.remove(this.widgetId);
  }
}

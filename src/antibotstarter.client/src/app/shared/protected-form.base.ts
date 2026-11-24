import { Directive, OnInit, inject, signal, viewChild } from '@angular/core';
import { ApiConfigService } from '../core/api-config.service';
import { FormPurpose, FormTokenService } from '../core/form-token.service';
import { TurnstileComponent } from './turnstile.component';

/**
 * Shared logic of the protected forms: form token, CAPTCHA and honeypot.
 * After every submission (successful or not) a new token and a new CAPTCHA are requested, because both are single-use.
 */
@Directive()
export abstract class ProtectedFormBase implements OnInit {
  protected abstract readonly purpose: FormPurpose;

  protected readonly apiConfig = inject(ApiConfigService);
  private readonly formTokens = inject(FormTokenService);

  readonly formToken = signal<string | null>(null);
  readonly captchaToken = signal<string | null>(null);
  readonly submitting = signal(false);
  readonly error = signal<string | null>(null);

  private readonly turnstile = viewChild(TurnstileComponent);

  ngOnInit(): void {
    this.refreshFormToken();
  }

  get captchaEnabled(): boolean {
    return this.apiConfig.config().captchaEnabled;
  }

  get siteKey(): string {
    return this.apiConfig.config().turnstileSiteKey;
  }

  /** The button is enabled only with a form token and (when applicable) a solved CAPTCHA. */
  get securityReady(): boolean {
    return !!this.formToken() && (!this.captchaEnabled || !!this.captchaToken());
  }

  onCaptcha(token: string | null): void {
    this.captchaToken.set(token);
  }

  protected refreshFormToken(): void {
    this.formToken.set(null);
    this.formTokens.get(this.purpose).subscribe({
      next: (t) => this.formToken.set(t),
      error: () => this.error.set('The form could not be prepared. Please reload the page.'),
    });
  }

  protected resetSecurity(): void {
    this.refreshFormToken();
    this.turnstile()?.reset();
    this.captchaToken.set(null);
  }
}

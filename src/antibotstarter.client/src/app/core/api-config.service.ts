import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface ClientConfig {
  captchaEnabled: boolean;
  turnstileSiteKey: string;
}

/**
 * App startup: loads the public configuration and gets the antiforgery cookies
 * (XSRF-TOKEN) that HttpClient sends back as X-XSRF-TOKEN on every POST.
 */
@Injectable({ providedIn: 'root' })
export class ApiConfigService {
  private readonly http = inject(HttpClient);

  readonly config = signal<ClientConfig>({ captchaEnabled: true, turnstileSiteKey: '' });

  async init(): Promise<void> {
    try {
      this.config.set(await firstValueFrom(this.http.get<ClientConfig>('/api/config')));
    } catch {
      // Keep captchaEnabled = true: without config the server still requires the CAPTCHA.
    }
    await this.refreshAntiforgery();
  }

  /** Must be repeated after login/logout: the antiforgery token is bound to the identity. */
  async refreshAntiforgery(): Promise<void> {
    try {
      await firstValueFrom(this.http.get('/api/antiforgery'));
    } catch {
      /* the next POST fails with antiforgery_failed and the error is shown */
    }
  }
}

import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, from, map, switchMap } from 'rxjs';
import { ApiConfigService } from './api-config.service';

export interface MfaStatus {
  enabled: boolean;
  recoveryCodesLeft: number;
}

export interface MfaSetup {
  sharedKey: string;
  authenticatorUri: string;
}

/** Two-factor authentication (TOTP) management for the signed-in user. */
@Injectable({ providedIn: 'root' })
export class MfaService {
  private readonly http = inject(HttpClient);
  private readonly apiConfig = inject(ApiConfigService);

  status(): Observable<MfaStatus> {
    return this.http.get<MfaStatus>('/api/account/mfa');
  }

  setup(): Observable<MfaSetup> {
    return this.http.post<MfaSetup>('/api/account/mfa/setup', {});
  }

  /** Returns the recovery codes. The server refreshes the sign-in, so the antiforgery token is renewed. */
  enable(code: string): Observable<string[]> {
    return this.http.post<{ recoveryCodes: string[] }>('/api/account/mfa/enable', { code }).pipe(
      switchMap((r) => from(this.apiConfig.refreshAntiforgery()).pipe(map(() => r.recoveryCodes))),
    );
  }

  disable(code: string): Observable<void> {
    return this.http
      .post<void>('/api/account/mfa/disable', { code })
      .pipe(switchMap(() => from(this.apiConfig.refreshAntiforgery())));
  }

  regenerateRecoveryCodes(code: string): Observable<string[]> {
    return this.http.post<{ recoveryCodes: string[] }>('/api/account/mfa/recovery-codes', { code }).pipe(map((r) => r.recoveryCodes));
  }
}

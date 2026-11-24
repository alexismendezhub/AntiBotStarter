import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, catchError, from, map, of, switchMap, tap } from 'rxjs';
import { ApiConfigService } from './api-config.service';

export interface ProtectedFormFields {
  website: string;
  formToken: string;
  captchaToken: string | null;
}

export interface LoginRequest extends ProtectedFormFields {
  email: string;
  password: string;
  rememberMe: boolean;
}

export interface RegisterRequest extends ProtectedFormFields {
  email: string;
  password: string;
}

export interface EmailRequest extends ProtectedFormFields {
  email: string;
}

export interface ResetPasswordRequest extends ProtectedFormFields {
  email: string;
  code: string;
  newPassword: string;
}

export interface LoginOutcome {
  requiresTwoFactor: boolean;
}

interface LoginResponse {
  email?: string;
  requiresTwoFactor?: boolean;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly apiConfig = inject(ApiConfigService);

  readonly currentUser = signal<string | null>(null);

  /** Password step. With 2FA enabled the user is NOT signed in yet: call loginTwoFactor/loginRecovery next. */
  login(req: LoginRequest): Observable<LoginOutcome> {
    return this.http.post<LoginResponse>('/api/auth/login', req).pipe(
      switchMap((r) => (r.requiresTwoFactor ? of({ requiresTwoFactor: true }) : this.signedIn(r.email ?? null))),
    );
  }

  loginTwoFactor(code: string, rememberMachine: boolean): Observable<LoginOutcome> {
    return this.http
      .post<LoginResponse>('/api/auth/login-2fa', { code, rememberMachine })
      .pipe(switchMap((r) => this.signedIn(r.email ?? null)));
  }

  loginRecovery(recoveryCode: string): Observable<LoginOutcome> {
    return this.http
      .post<LoginResponse>('/api/auth/login-recovery', { recoveryCode })
      .pipe(switchMap((r) => this.signedIn(r.email ?? null)));
  }

  register(req: RegisterRequest): Observable<string> {
    return this.http.post<{ message: string }>('/api/auth/register', req).pipe(map((r) => r.message));
  }

  resendConfirmation(req: EmailRequest): Observable<string> {
    return this.http.post<{ message: string }>('/api/auth/resend-confirmation', req).pipe(map((r) => r.message));
  }

  forgotPassword(req: EmailRequest): Observable<string> {
    return this.http.post<{ message: string }>('/api/auth/forgot-password', req).pipe(map((r) => r.message));
  }

  resetPassword(req: ResetPasswordRequest): Observable<void> {
    return this.http.post<void>('/api/auth/reset-password', req);
  }

  confirmEmail(userId: string, code: string): Observable<void> {
    return this.http.post<void>('/api/auth/confirm-email', { userId, code });
  }

  logout(): Observable<void> {
    return this.http.post<void>('/api/auth/logout', {}).pipe(
      tap(() => this.currentUser.set(null)),
      switchMap(() => from(this.apiConfig.refreshAntiforgery())),
    );
  }

  me(): Observable<string | null> {
    return this.http.get<{ email: string }>('/api/auth/me').pipe(
      map((r) => r.email),
      tap((email) => this.currentUser.set(email)),
      catchError(() => {
        this.currentUser.set(null);
        return of(null);
      }),
    );
  }

  /** The antiforgery token is bound to the identity: renew it after every sign-in. */
  private signedIn(email: string | null): Observable<LoginOutcome> {
    this.currentUser.set(email);
    return from(this.apiConfig.refreshAntiforgery()).pipe(map(() => ({ requiresTwoFactor: false })));
  }
}

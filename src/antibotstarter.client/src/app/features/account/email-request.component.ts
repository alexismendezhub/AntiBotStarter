import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, input, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Observable } from 'rxjs';
import { AuthService, EmailRequest } from '../../core/auth.service';
import { FormPurpose } from '../../core/form-token.service';
import { ProtectedFormBase } from '../../shared/protected-form.base';
import { TurnstileComponent } from '../../shared/turnstile.component';

/**
 * "Forgot password" and "Resend confirmation email": one email field with the same protections as login
 * (form token, honeypot, CAPTCHA). The server answers the same whether or not the account exists.
 */
@Component({
  selector: 'app-email-request',
  imports: [ReactiveFormsModule, RouterLink, TurnstileComponent],
  template: `
    <h1>{{ title() }}</h1>
    @if (done()) {
      <p class="ok" role="status">{{ done() }}</p>
      <p><a routerLink="/login">Back to sign in</a></p>
    } @else {
      <p>{{ intro() }}</p>
      <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
        <label>
          Email
          <input type="email" formControlName="email" autocomplete="email" required>
        </label>
        <div class="hp-field" aria-hidden="true">
          <label>
            Website
            <input type="text" formControlName="website" name="website" tabindex="-1" autocomplete="off" data-testid="honeypot">
          </label>
        </div>
        @if (captchaEnabled) {
          <app-turnstile [siteKey]="siteKey" [action]="purpose" (token)="onCaptcha($event)" />
        }
        @if (error()) {
          <p class="error" role="alert">{{ error() }}</p>
        }
        <button type="submit" [disabled]="form.invalid || !securityReady || submitting()">Send</button>
      </form>
    }
  `,
})
export class EmailRequestComponent extends ProtectedFormBase {
  /** Set from the route data. */
  readonly kind = input.required<'forgot' | 'resend'>();

  protected get purpose(): FormPurpose {
    return this.kind();
  }

  private readonly fb = inject(NonNullableFormBuilder);
  private readonly auth = inject(AuthService);

  readonly done = signal<string | null>(null);

  readonly form = this.fb.group({
    email: ['', [Validators.required, Validators.email, Validators.maxLength(254)]],
    website: [''], // honeypot
  });

  title(): string {
    return this.kind() === 'forgot' ? 'Reset your password' : 'Resend the confirmation email';
  }

  intro(): string {
    return this.kind() === 'forgot'
      ? 'Enter the email of your account and we will send you a link to choose a new password.'
      : 'Enter the email you registered with and we will send the confirmation link again.';
  }

  submit(): void {
    if (this.form.invalid || !this.securityReady || this.submitting()) return;
    this.submitting.set(true);
    this.error.set(null);
    const req: EmailRequest = { ...this.form.getRawValue(), formToken: this.formToken()!, captchaToken: this.captchaToken() };
    const call: Observable<string> = this.kind() === 'forgot' ? this.auth.forgotPassword(req) : this.auth.resendConfirmation(req);
    call.subscribe({
      next: (message) => {
        this.submitting.set(false);
        this.done.set(message);
      },
      error: (err: HttpErrorResponse) => {
        this.submitting.set(false);
        this.error.set(err.error?.message ?? 'The request could not be sent.');
        this.resetSecurity();
      },
    });
  }
}

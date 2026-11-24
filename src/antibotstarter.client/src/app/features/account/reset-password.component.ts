import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { FormPurpose } from '../../core/form-token.service';
import { ProtectedFormBase } from '../../shared/protected-form.base';
import { TurnstileComponent } from '../../shared/turnstile.component';
import { PASSWORD_PATTERN } from '../register/register.component';

/** Opened from the emailed link (/reset-password?email=…&code=…). */
@Component({
  selector: 'app-reset-password',
  imports: [ReactiveFormsModule, RouterLink, TurnstileComponent],
  template: `
    <h1>Choose a new password</h1>
    @if (done()) {
      <p class="ok" role="status">Your password was changed. <a routerLink="/login">Sign in</a>.</p>
    } @else if (!linkOk()) {
      <p class="error">The link is incomplete. <a routerLink="/forgot-password">Request a new one</a>.</p>
    } @else {
      <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
        <label>
          New password (at least 12 characters, with uppercase, lowercase, digit and symbol)
          <input type="password" formControlName="newPassword" autocomplete="new-password" required>
        </label>
        <div class="hp-field" aria-hidden="true">
          <label>
            Website
            <input type="text" formControlName="website" name="website" tabindex="-1" autocomplete="off" data-testid="honeypot">
          </label>
        </div>
        @if (captchaEnabled) {
          <app-turnstile [siteKey]="siteKey" action="reset" (token)="onCaptcha($event)" />
        }
        @if (error()) {
          <p class="error" role="alert">{{ error() }}</p>
          @for (d of details(); track d) {
            <p class="error">{{ d }}</p>
          }
        }
        <button type="submit" [disabled]="form.invalid || !securityReady || submitting()">Change password</button>
      </form>
    }
  `,
})
export class ResetPasswordComponent extends ProtectedFormBase implements OnInit {
  protected readonly purpose: FormPurpose = 'reset';

  private readonly fb = inject(NonNullableFormBuilder);
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);

  readonly done = signal(false);
  readonly linkOk = signal(false);
  readonly details = signal<string[]>([]);
  private email = '';
  private code = '';

  readonly form = this.fb.group({
    newPassword: ['', [Validators.required, Validators.pattern(PASSWORD_PATTERN)]],
    website: [''], // honeypot
  });

  override ngOnInit(): void {
    const q = this.route.snapshot.queryParamMap;
    this.email = q.get('email') ?? '';
    this.code = q.get('code') ?? '';
    this.linkOk.set(!!this.email && !!this.code);
    super.ngOnInit();
  }

  submit(): void {
    if (this.form.invalid || !this.securityReady || this.submitting()) return;
    this.submitting.set(true);
    this.error.set(null);
    this.details.set([]);
    this.auth
      .resetPassword({ ...this.form.getRawValue(), email: this.email, code: this.code, formToken: this.formToken()!, captchaToken: this.captchaToken() })
      .subscribe({
        next: () => {
          this.submitting.set(false);
          this.done.set(true);
        },
        error: (err: HttpErrorResponse) => {
          this.submitting.set(false);
          this.error.set(err.error?.message ?? 'The password could not be changed.');
          this.details.set(Array.isArray(err.error?.details) ? err.error.details : []);
          this.resetSecurity();
        },
      });
  }
}

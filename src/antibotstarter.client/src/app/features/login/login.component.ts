import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { Observable } from 'rxjs';
import { AuthService, LoginOutcome } from '../../core/auth.service';
import { FormPurpose } from '../../core/form-token.service';
import { ProtectedFormBase } from '../../shared/protected-form.base';
import { TurnstileComponent } from '../../shared/turnstile.component';

export type LoginStep = 'password' | 'code' | 'recovery';

@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule, RouterLink, TurnstileComponent],
  templateUrl: './login.component.html',
})
export class LoginComponent extends ProtectedFormBase {
  protected readonly purpose: FormPurpose = 'login';

  private readonly fb = inject(NonNullableFormBuilder);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly step = signal<LoginStep>('password');

  readonly form = this.fb.group({
    email: ['', [Validators.required, Validators.email, Validators.maxLength(254)]],
    password: ['', [Validators.required, Validators.maxLength(256)]],
    rememberMe: [false],
    website: [''], // honeypot
  });

  readonly codeForm = this.fb.group({
    code: ['', [Validators.required, Validators.pattern(/^\s*\d{3}[\s-]?\d{3}\s*$/)]],
    rememberMachine: [false],
  });

  readonly recoveryForm = this.fb.group({
    recoveryCode: ['', [Validators.required, Validators.maxLength(64)]],
  });

  submit(): void {
    if (this.form.invalid || !this.securityReady || this.submitting()) return;
    this.run(
      this.auth.login({ ...this.form.getRawValue(), formToken: this.formToken()!, captchaToken: this.captchaToken() }),
      () => this.resetSecurity(),
    );
  }

  submitCode(): void {
    if (this.codeForm.invalid || this.submitting()) return;
    const { code, rememberMachine } = this.codeForm.getRawValue();
    this.run(this.auth.loginTwoFactor(code, rememberMachine), () => this.codeForm.controls.code.reset());
  }

  submitRecovery(): void {
    if (this.recoveryForm.invalid || this.submitting()) return;
    this.run(this.auth.loginRecovery(this.recoveryForm.getRawValue().recoveryCode), () => this.recoveryForm.reset());
  }

  useRecoveryCode(): void {
    this.error.set(null);
    this.step.set('recovery');
  }

  private run(call: Observable<LoginOutcome>, onError: () => void): void {
    this.submitting.set(true);
    this.error.set(null);
    call.subscribe({
      next: (outcome) => {
        this.submitting.set(false);
        if (outcome.requiresTwoFactor) {
          this.step.set('code');
          return;
        }
        void this.router.navigate(['/products']);
      },
      error: (err: HttpErrorResponse) => {
        this.submitting.set(false);
        if (err.error?.error === 'two_factor_expired') {
          // The 5-minute window between the two steps expired: start again.
          this.step.set('password');
          this.resetSecurity();
          this.error.set('The sign-in took too long. Please enter your password again.');
          return;
        }
        this.error.set(err.error?.message ?? 'Could not sign in.');
        onError();
      },
    });
  }
}

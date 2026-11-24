import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { FormPurpose } from '../../core/form-token.service';
import { ProtectedFormBase } from '../../shared/protected-form.base';
import { TurnstileComponent } from '../../shared/turnstile.component';

/** Same rules as IdentityOptions on the server (the server has the final say). */
export const PASSWORD_PATTERN = /^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^A-Za-z0-9]).{12,256}$/;

@Component({
  selector: 'app-register',
  imports: [ReactiveFormsModule, RouterLink, TurnstileComponent],
  templateUrl: './register.component.html',
})
export class RegisterComponent extends ProtectedFormBase {
  protected readonly purpose: FormPurpose = 'register';

  private readonly fb = inject(NonNullableFormBuilder);
  private readonly auth = inject(AuthService);

  readonly done = signal<string | null>(null);
  readonly details = signal<string[]>([]);

  readonly form = this.fb.group({
    email: ['', [Validators.required, Validators.email, Validators.maxLength(254)]],
    password: ['', [Validators.required, Validators.pattern(PASSWORD_PATTERN)]],
    website: [''], // honeypot
  });

  submit(): void {
    if (this.form.invalid || !this.securityReady || this.submitting()) return;
    this.submitting.set(true);
    this.error.set(null);
    this.details.set([]);

    this.auth
      .register({ ...this.form.getRawValue(), formToken: this.formToken()!, captchaToken: this.captchaToken() })
      .subscribe({
        next: (message) => {
          this.submitting.set(false);
          this.done.set(message);
        },
        error: (err: HttpErrorResponse) => {
          this.submitting.set(false);
          this.error.set(err.error?.message ?? 'Registration could not be completed.');
          this.details.set(Array.isArray(err.error?.details) ? err.error.details : []);
          this.resetSecurity();
        },
      });
  }
}

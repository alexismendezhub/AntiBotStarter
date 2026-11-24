import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { toDataURL } from 'qrcode';
import { Observable } from 'rxjs';
import { MfaService, MfaSetup, MfaStatus } from '../../core/mfa.service';

const CODE_PATTERN = /^\s*\d{3}[\s-]?\d{3}\s*$/;

/** Two-factor authentication: set up with a QR code, show recovery codes once, disable with a current code. */
@Component({
  selector: 'app-account-security',
  imports: [ReactiveFormsModule],
  template: `
    <h1>Account security</h1>

    @if (status(); as s) {
      @if (recoveryCodes(); as codes) {
        <section class="notice" data-testid="recovery-codes">
          <p><strong>Save these recovery codes now.</strong> Each one signs you in once if you lose your phone. They will not be shown again.</p>
          <ul>
            @for (c of codes; track c) {
              <li><code>{{ c }}</code></li>
            }
          </ul>
        </section>
      }

      @if (s.enabled) {
        <p class="ok">Two-factor authentication is <strong>on</strong>. Recovery codes left: {{ s.recoveryCodesLeft }}.</p>
        <form [formGroup]="codeForm" novalidate>
          <label>
            Current code from your authenticator app
            <input type="text" formControlName="code" inputmode="numeric" autocomplete="one-time-code" maxlength="7">
          </label>
          <button type="button" (click)="regenerate()" [disabled]="codeForm.invalid || busy()">New recovery codes</button>
          <button type="button" (click)="disable()" [disabled]="codeForm.invalid || busy()">Turn off</button>
        </form>
      } @else if (setup(); as setup) {
        <p>Scan this QR code with your authenticator app (Microsoft Authenticator, Google Authenticator, 1Password…),
          or type the key manually.</p>
        @if (qr()) {
          <img [src]="qr()" alt="QR code for the authenticator app" width="200" height="200" data-testid="qr">
        }
        <p>Key: <code data-testid="shared-key">{{ setup.sharedKey }}</code></p>
        <form [formGroup]="codeForm" (ngSubmit)="enable()" novalidate>
          <label>
            Code shown by the app
            <input type="text" formControlName="code" inputmode="numeric" autocomplete="one-time-code" maxlength="7">
          </label>
          <button type="submit" [disabled]="codeForm.invalid || busy()">Turn on</button>
        </form>
      } @else {
        <p>Two-factor authentication is <strong>off</strong>. Turn it on so a stolen password is not enough to enter your account.</p>
        <button type="button" (click)="startSetup()" [disabled]="busy()">Set up two-factor authentication</button>
      }
    }

    @if (error()) {
      <p class="error" role="alert">{{ error() }}</p>
    }
  `,
})
export class AccountSecurityComponent implements OnInit {
  private readonly mfa = inject(MfaService);
  private readonly fb = inject(NonNullableFormBuilder);

  readonly status = signal<MfaStatus | null>(null);
  readonly setup = signal<MfaSetup | null>(null);
  readonly qr = signal<string | null>(null);
  readonly recoveryCodes = signal<string[] | null>(null);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);

  readonly codeForm = this.fb.group({ code: ['', [Validators.required, Validators.pattern(CODE_PATTERN)]] });

  ngOnInit(): void {
    this.refresh();
  }

  startSetup(): void {
    this.busy.set(true);
    this.error.set(null);
    this.mfa.setup().subscribe({
      next: async (s) => {
        this.setup.set(s);
        this.busy.set(false);
        try {
          this.qr.set(await toDataURL(s.authenticatorUri, { errorCorrectionLevel: 'M', margin: 1, width: 200 }));
        } catch {
          this.qr.set(null); // the key is still shown for manual entry
        }
      },
      error: (e: HttpErrorResponse) => this.fail(e),
    });
  }

  enable(): void {
    this.act(this.mfa.enable(this.codeForm.getRawValue().code), (codes) => {
      this.recoveryCodes.set(codes);
      this.setup.set(null);
      this.qr.set(null);
    });
  }

  disable(): void {
    this.act(this.mfa.disable(this.codeForm.getRawValue().code), () => this.recoveryCodes.set(null));
  }

  regenerate(): void {
    this.act(this.mfa.regenerateRecoveryCodes(this.codeForm.getRawValue().code), (codes) => this.recoveryCodes.set(codes));
  }

  private act<T>(call: Observable<T>, onOk: (value: T) => void): void {
    if (this.codeForm.invalid || this.busy()) return;
    this.busy.set(true);
    this.error.set(null);
    call.subscribe({
      next: (value) => {
        this.busy.set(false);
        this.codeForm.reset();
        onOk(value);
        this.refresh();
      },
      error: (e: HttpErrorResponse) => this.fail(e),
    });
  }

  private refresh(): void {
    this.mfa.status().subscribe({ next: (s) => this.status.set(s), error: (e: HttpErrorResponse) => this.fail(e) });
  }

  private fail(e: HttpErrorResponse): void {
    this.busy.set(false);
    this.error.set(e.error?.error === 'invalid_code' ? 'That code is not valid. Try the current one.' : 'The request failed. Please try again.');
  }
}

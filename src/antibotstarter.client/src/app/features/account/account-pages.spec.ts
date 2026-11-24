import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { ApiConfigService } from '../../core/api-config.service';
import { AccountSecurityComponent } from './account-security.component';
import { EmailRequestComponent } from './email-request.component';
import { ResetPasswordComponent } from './reset-password.component';

function configure(component: unknown, query: Record<string, string> = {}): HttpTestingController {
  TestBed.configureTestingModule({
    imports: [component as never],
    providers: [
      provideRouter([]),
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap(query) } } },
    ],
  });
  TestBed.inject(ApiConfigService).config.set({ captchaEnabled: false, turnstileSiteKey: '' });
  return TestBed.inject(HttpTestingController);
}

describe('EmailRequestComponent', () => {
  let mock: HttpTestingController;
  let fixture: ComponentFixture<EmailRequestComponent>;

  async function create(kind: 'forgot' | 'resend'): Promise<void> {
    mock = configure(EmailRequestComponent);
    fixture = TestBed.createComponent(EmailRequestComponent);
    fixture.componentRef.setInput('kind', kind);
    fixture.detectChanges();
    const token = mock.expectOne((r) => r.url === '/api/forms/token');
    expect(token.request.params.get('purpose')).toBe(kind);
    token.flush({ token: 'ft-' + kind });
    await fixture.whenStable();
    fixture.detectChanges();
  }

  afterEach(() => mock.verify());

  it('forgot: posts email, form token and honeypot, then shows the generic answer', async () => {
    await create('forgot');
    fixture.componentInstance.form.setValue({ email: 'a@example.com', website: '' });
    fixture.componentInstance.submit();
    const req = mock.expectOne('/api/auth/forgot-password');
    expect(req.request.body).toEqual(jasmine.objectContaining({ email: 'a@example.com', website: '', formToken: 'ft-forgot' }));
    req.flush({ message: 'If the address belongs to an account that needs it, you will receive an email shortly.' }, { status: 202, statusText: 'Accepted' });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('you will receive an email');
  });

  it('resend: posts to resend-confirmation with its own purpose', async () => {
    await create('resend');
    expect(fixture.nativeElement.textContent).toContain('Resend the confirmation email');
    fixture.componentInstance.form.setValue({ email: 'b@example.com', website: '' });
    fixture.componentInstance.submit();
    expect(mock.expectOne('/api/auth/resend-confirmation').request.body.formToken).toBe('ft-resend');
  });

  it('on error shows it and renews the single-use form token', async () => {
    await create('forgot');
    fixture.componentInstance.form.setValue({ email: 'a@example.com', website: '' });
    fixture.componentInstance.submit();
    mock.expectOne('/api/auth/forgot-password').flush({ error: 'request_rejected', message: 'The request could not be processed.' }, { status: 400, statusText: 'Bad Request' });
    mock.expectOne((r) => r.url === '/api/forms/token').flush({ token: 'ft-2' });
    expect(fixture.componentInstance.error()).toContain('could not be processed');
  });
});

describe('ResetPasswordComponent', () => {
  let mock: HttpTestingController;

  afterEach(() => mock.verify());

  async function create(query: Record<string, string>): Promise<ComponentFixture<ResetPasswordComponent>> {
    mock = configure(ResetPasswordComponent, query);
    const fixture = TestBed.createComponent(ResetPasswordComponent);
    fixture.detectChanges();
    mock.expectOne((r) => r.url === '/api/forms/token' && r.params.get('purpose') === 'reset').flush({ token: 'ft-reset' });
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  it('sends the email and code from the link with the new password', async () => {
    const fixture = await create({ email: 'r@example.com', code: 'CODE123' });
    fixture.componentInstance.form.setValue({ newPassword: 'Correct-Long-2026!', website: '' });
    fixture.componentInstance.submit();
    const req = mock.expectOne('/api/auth/reset-password');
    expect(req.request.body).toEqual(jasmine.objectContaining({ email: 'r@example.com', code: 'CODE123', newPassword: 'Correct-Long-2026!', formToken: 'ft-reset' }));
    req.flush({ reset: true });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Your password was changed');
  });

  it('an incomplete link shows how to get a new one', async () => {
    const fixture = await create({});
    expect(fixture.nativeElement.textContent).toContain('The link is incomplete');
  });

  it('shows the password rule details from the server', async () => {
    const fixture = await create({ email: 'r@example.com', code: 'CODE123' });
    fixture.componentInstance.form.setValue({ newPassword: 'Correct-Long-2026!', website: '' });
    fixture.componentInstance.submit();
    mock.expectOne('/api/auth/reset-password').flush(
      { error: 'invalid_password', details: ['This password appears in known data breaches.'] },
      { status: 400, statusText: 'Bad Request' },
    );
    mock.expectOne((r) => r.url === '/api/forms/token').flush({ token: 'ft-2' });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('data breaches');
  });
});

describe('AccountSecurityComponent', () => {
  let mock: HttpTestingController;
  let fixture: ComponentFixture<AccountSecurityComponent>;

  beforeEach(() => {
    mock = configure(AccountSecurityComponent);
    fixture = TestBed.createComponent(AccountSecurityComponent);
  });

  afterEach(() => mock.verify());

  async function settle(): Promise<void> {
    await new Promise((r) => setTimeout(r, 50));
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('sets up 2FA: shows a QR code and the key, then the recovery codes once', async () => {
    fixture.detectChanges();
    mock.expectOne('/api/account/mfa').flush({ enabled: false, recoveryCodesLeft: 0 });
    fixture.detectChanges();

    fixture.componentInstance.startSetup();
    mock.expectOne('/api/account/mfa/setup').flush({
      sharedKey: 'abcd efgh ijkl mnop',
      authenticatorUri: 'otpauth://totp/AntiBotStarter:a%40example.com?secret=ABCDEFGHIJKLMNOP&issuer=AntiBotStarter&digits=6',
    });
    await settle();

    const qr = fixture.nativeElement.querySelector('[data-testid=qr]') as HTMLImageElement;
    expect(qr.src).toMatch(/^data:image\/png;base64,/);
    expect(fixture.nativeElement.querySelector('[data-testid=shared-key]').textContent).toContain('abcd efgh');

    fixture.componentInstance.codeForm.setValue({ code: '123456' });
    fixture.componentInstance.enable();
    expect(mock.expectOne('/api/account/mfa/enable').request.body).toEqual({ code: '123456' });
  });

  it('after enabling shows the recovery codes and refreshes the status', async () => {
    fixture.detectChanges();
    mock.expectOne('/api/account/mfa').flush({ enabled: false, recoveryCodesLeft: 0 });
    fixture.componentInstance.codeForm.setValue({ code: '123456' });
    fixture.componentInstance.enable();
    mock.expectOne('/api/account/mfa/enable').flush({ recoveryCodes: ['aaaa-1111', 'bbbb-2222'] });
    await settle();
    mock.expectOne('/api/antiforgery').flush(null, { status: 204, statusText: 'No Content' });
    await settle();
    mock.expectOne('/api/account/mfa').flush({ enabled: true, recoveryCodesLeft: 2 });
    fixture.detectChanges();

    const codes = fixture.nativeElement.querySelector('[data-testid=recovery-codes]');
    expect(codes.textContent).toContain('aaaa-1111');
    expect(fixture.nativeElement.textContent).toContain('is on');
  });

  it('a wrong code shows a clear error', async () => {
    fixture.detectChanges();
    mock.expectOne('/api/account/mfa').flush({ enabled: true, recoveryCodesLeft: 10 });
    fixture.componentInstance.codeForm.setValue({ code: '000000' });
    fixture.componentInstance.disable();
    mock.expectOne('/api/account/mfa/disable').flush({ error: 'invalid_code' }, { status: 400, statusText: 'Bad Request' });
    expect(fixture.componentInstance.error()).toContain('not valid');
  });

  it('rejects malformed codes before calling the server', () => {
    fixture.detectChanges();
    mock.expectOne('/api/account/mfa').flush({ enabled: true, recoveryCodesLeft: 10 });
    fixture.componentInstance.codeForm.setValue({ code: '12ab' });
    fixture.componentInstance.disable();
    mock.expectNone('/api/account/mfa/disable');
  });
});

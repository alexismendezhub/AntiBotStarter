import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { ApiConfigService } from '../../core/api-config.service';
import { TurnstileLoaderService } from '../../core/turnstile-loader.service';
import { FakeTurnstile, fakeTurnstileLoader } from '../../testing/fake-turnstile';
import { LoginComponent } from './login.component';

describe('LoginComponent', () => {
  let fixture: ComponentFixture<LoginComponent>;
  let component: LoginComponent;
  let mock: HttpTestingController;
  let turnstile: FakeTurnstile;

  async function setup(captchaEnabled: boolean): Promise<void> {
    turnstile = new FakeTurnstile();
    TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: TurnstileLoaderService, useValue: fakeTurnstileLoader(turnstile) },
      ],
    });
    TestBed.inject(ApiConfigService).config.set({ captchaEnabled, turnstileSiteKey: 'site-key' });
    spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
    mock = TestBed.inject(HttpTestingController);

    fixture = TestBed.createComponent(LoginComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();

    const tokenReq = mock.expectOne((r) => r.url === '/api/forms/token');
    expect(tokenReq.request.params.get('purpose')).toBe('login');
    tokenReq.flush({ token: 'form-token-1' });
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function fill(): void {
    component.form.setValue({ email: 'alice@example.com', password: 'Secret-Long-1!x', rememberMe: false, website: '' });
  }

  function button(): HTMLButtonElement {
    return fixture.nativeElement.querySelector('button[type=submit]');
  }

  afterEach(() => mock.verify());

  it('renders Turnstile with the site key and action', async () => {
    await setup(true);
    expect(turnstile.lastOptions?.sitekey).toBe('site-key');
    expect(turnstile.lastOptions?.action).toBe('login');
  });

  it('does not submit until the CAPTCHA is solved', async () => {
    await setup(true);
    fill();
    fixture.detectChanges();
    expect(button().disabled).toBeTrue();

    component.submit();
    mock.expectNone('/api/auth/login');

    turnstile.solve('captcha-1');
    fixture.detectChanges();
    expect(button().disabled).toBeFalse();
  });

  it('sends form token, CAPTCHA and an empty honeypot', async () => {
    await setup(true);
    fill();
    turnstile.solve('captcha-1');
    component.submit();

    const req = mock.expectOne('/api/auth/login');
    expect(req.request.body).toEqual(jasmine.objectContaining({
      email: 'alice@example.com',
      website: '',
      formToken: 'form-token-1',
      captchaToken: 'captcha-1',
    }));
    req.flush({ email: 'alice@example.com' });
    await new Promise((r) => setTimeout(r));
    mock.expectOne('/api/antiforgery').flush(null, { status: 204, statusText: 'No Content' });
    await new Promise((r) => setTimeout(r)); // flush the promise chain (login → refreshAntiforgery → navigate)
    expect(TestBed.inject(Router).navigate).toHaveBeenCalledWith(['/products']);
  });

  it('after an error shows the message and renews token and CAPTCHA (both single-use)', async () => {
    await setup(true);
    fill();
    turnstile.solve('captcha-1');
    component.submit();

    mock.expectOne('/api/auth/login').flush({ message: 'Wrong email or password' }, { status: 401, statusText: 'Unauthorized' });
    mock.expectOne((r) => r.url === '/api/forms/token').flush({ token: 'form-token-2' });

    expect(component.error()).toContain('Wrong email');
    expect(component.formToken()).toBe('form-token-2');
    expect(component.captchaToken()).toBeNull();
    expect(turnstile.resets).toBe(1);
  });

  it('an expired CAPTCHA disables submission again', async () => {
    await setup(true);
    fill();
    turnstile.solve('captcha-1');
    turnstile.expire();
    fixture.detectChanges();
    expect(button().disabled).toBeTrue();
  });

  it('without CAPTCHA the form token is enough', async () => {
    await setup(false);
    fill();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('app-turnstile')).toBeNull();
    expect(button().disabled).toBeFalse();
  });

  it('the honeypot is off-screen and out of the tab order', async () => {
    await setup(true);
    const hp: HTMLInputElement = fixture.nativeElement.querySelector('[data-testid=honeypot]');
    expect(hp.getAttribute('tabindex')).toBe('-1');
    expect(hp.getAttribute('autocomplete')).toBe('off');
    expect(hp.closest('.hp-field')?.getAttribute('aria-hidden')).toBe('true');
  });

  async function passwordStepRequiringTwoFactor(): Promise<void> {
    await setup(false);
    fill();
    component.submit();
    mock.expectOne('/api/auth/login').flush({ requiresTwoFactor: true });
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('with 2FA enabled shows the code step and does not sign in yet', async () => {
    await passwordStepRequiringTwoFactor();
    expect(component.step()).toBe('code');
    expect(fixture.nativeElement.querySelector('[data-testid=code-form]')).not.toBeNull();
    mock.expectNone('/api/antiforgery');
    expect(TestBed.inject(Router).navigate).not.toHaveBeenCalled();
  });

  it('the code step posts the TOTP code, renews antiforgery and navigates', async () => {
    await passwordStepRequiringTwoFactor();
    component.codeForm.setValue({ code: '123 456', rememberMachine: true });
    component.submitCode();

    const req = mock.expectOne('/api/auth/login-2fa');
    expect(req.request.body).toEqual({ code: '123 456', rememberMachine: true });
    req.flush({ email: 'alice@example.com' });
    await new Promise((r) => setTimeout(r));
    mock.expectOne('/api/antiforgery').flush(null, { status: 204, statusText: 'No Content' });
    await new Promise((r) => setTimeout(r));
    expect(TestBed.inject(Router).navigate).toHaveBeenCalledWith(['/products']);
  });

  it('a wrong code shows the error and clears the field', async () => {
    await passwordStepRequiringTwoFactor();
    component.codeForm.setValue({ code: '000000', rememberMachine: false });
    component.submitCode();
    mock.expectOne('/api/auth/login-2fa').flush({ error: 'invalid_code', message: 'The code is not valid' }, { status: 401, statusText: 'Unauthorized' });
    expect(component.error()).toContain('not valid');
    expect(component.codeForm.controls.code.value).toBe('');
    expect(component.step()).toBe('code');
  });

  it('can switch to a recovery code', async () => {
    await passwordStepRequiringTwoFactor();
    component.useRecoveryCode();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[data-testid=recovery-form]')).not.toBeNull();
    component.recoveryForm.setValue({ recoveryCode: 'ab12c-de34f' });
    component.submitRecovery();
    expect(mock.expectOne('/api/auth/login-recovery').request.body).toEqual({ recoveryCode: 'ab12c-de34f' });
  });

  it('an expired second step goes back to the password step with a new form token', async () => {
    await passwordStepRequiringTwoFactor();
    component.codeForm.setValue({ code: '123456', rememberMachine: false });
    component.submitCode();
    mock.expectOne('/api/auth/login-2fa').flush({ error: 'two_factor_expired' }, { status: 401, statusText: 'Unauthorized' });
    expect(component.step()).toBe('password');
    mock.expectOne((r) => r.url === '/api/forms/token').flush({ token: 'form-token-2' });
    expect(component.error()).toContain('took too long');
  });

  it('links to password recovery and resend confirmation', async () => {
    await setup(false);
    const links = Array.from(fixture.nativeElement.querySelectorAll('a')).map((a) => (a as HTMLAnchorElement).getAttribute('href'));
    expect(links).toContain('/forgot-password');
    expect(links).toContain('/resend-confirmation');
  });
});

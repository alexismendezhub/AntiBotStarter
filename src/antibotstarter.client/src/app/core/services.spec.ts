import { HttpClient, provideHttpClient, withXsrfConfiguration } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { MOCK_PLATFORM_LOCATION_CONFIG } from '@angular/common/testing';
import { TestBed } from '@angular/core/testing';
import { Router, UrlTree, provideRouter } from '@angular/router';
import { Observable, firstValueFrom } from 'rxjs';
import { ApiConfigService } from './api-config.service';
import { authGuard } from './auth.guard';
import { AuthService } from './auth.service';
import { FormTokenService } from './form-token.service';
import { XSRF_COOKIE, XSRF_HEADER } from './security.constants';

describe('core services', () => {
  let mock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    mock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => mock.verify());

  describe('FormTokenService', () => {
    it('requests the token with its purpose', async () => {
      const p = firstValueFrom(TestBed.inject(FormTokenService).get('register'));
      const req = mock.expectOne((r) => r.url === '/api/forms/token');
      expect(req.request.params.get('purpose')).toBe('register');
      req.flush({ token: 'abc' });
      expect(await p).toBe('abc');
    });
  });

  describe('ApiConfigService', () => {
    it('loads the config and then the antiforgery cookies', async () => {
      const svc = TestBed.inject(ApiConfigService);
      const done = svc.init();
      mock.expectOne('/api/config').flush({ captchaEnabled: false, turnstileSiteKey: 'k' });
      await Promise.resolve();
      await new Promise((r) => setTimeout(r));
      mock.expectOne('/api/antiforgery').flush(null, { status: 204, statusText: 'No Content' });
      await done;
      expect(svc.config()).toEqual({ captchaEnabled: false, turnstileSiteKey: 'k' });
    });

    it('keeps the CAPTCHA enabled when the config fails (fail safe)', async () => {
      const svc = TestBed.inject(ApiConfigService);
      const done = svc.init();
      mock.expectOne('/api/config').flush({}, { status: 500, statusText: 'Error' });
      await new Promise((r) => setTimeout(r));
      mock.expectOne('/api/antiforgery').flush(null, { status: 204, statusText: 'No Content' });
      await done;
      expect(svc.config().captchaEnabled).toBeTrue();
    });
  });

  describe('AuthService', () => {
    it('refreshes the antiforgery token after login (it is bound to the identity)', async () => {
      const auth = TestBed.inject(AuthService);
      const p = firstValueFrom(
        auth.login({ email: 'a@b.com', password: 'x', rememberMe: false, website: '', formToken: 't', captchaToken: 'c' }),
      );
      const login = mock.expectOne('/api/auth/login');
      expect(login.request.method).toBe('POST');
      expect(login.request.body.formToken).toBe('t');
      expect(login.request.body.captchaToken).toBe('c');
      expect(login.request.body.website).toBe('');
      login.flush({ email: 'a@b.com' });
      await new Promise((r) => setTimeout(r));
      mock.expectOne('/api/antiforgery').flush(null, { status: 204, statusText: 'No Content' });
      await p;
      expect(auth.currentUser()).toBe('a@b.com');
    });

    it('me() returns null on 401', async () => {
      const auth = TestBed.inject(AuthService);
      const p = firstValueFrom(auth.me());
      mock.expectOne('/api/auth/me').flush({}, { status: 401, statusText: 'Unauthorized' });
      expect(await p).toBeNull();
      expect(auth.currentUser()).toBeNull();
    });
  });

  describe('authGuard', () => {
    it('redirects to /login without a session', async () => {
      const result$ = TestBed.runInInjectionContext(() => authGuard({} as never, {} as never)) as Observable<boolean | UrlTree>;
      const p = firstValueFrom(result$);
      mock.expectOne('/api/auth/me').flush({}, { status: 401, statusText: 'Unauthorized' });
      const result = await p;
      expect(TestBed.inject(Router).serializeUrl(result as UrlTree)).toBe('/login');
    });

    it('lets a signed-in user through', async () => {
      const p = firstValueFrom(TestBed.runInInjectionContext(() => authGuard({} as never, {} as never)) as Observable<boolean | UrlTree>);
      mock.expectOne('/api/auth/me').flush({ email: 'a@b.com' });
      expect(await p).toBeTrue();
    });
  });
});

describe('XSRF', () => {
  it('HttpClient sends X-XSRF-TOKEN from the cookie on POSTs', () => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withXsrfConfiguration({ cookieName: XSRF_COOKIE, headerName: XSRF_HEADER })),
        provideHttpClientTesting(),
        // TestBed's MockPlatformLocation has no URL by default, and Angular's XSRF interceptor skips the
        // header when it cannot resolve the page origin. Give it the real page origin, as in a browser.
        { provide: MOCK_PLATFORM_LOCATION_CONFIG, useValue: { startUrl: location.origin + '/' } },
      ],
    });
    document.cookie = `${XSRF_COOKIE}=xsrf-value; path=/`;
    const mock = TestBed.inject(HttpTestingController);

    TestBed.inject(HttpClient).post('/api/auth/login', {}).subscribe();
    const req = mock.expectOne('/api/auth/login');
    expect(req.request.headers.get(XSRF_HEADER)).toBe('xsrf-value');
    req.flush({});

    document.cookie = `${XSRF_COOKIE}=; expires=Thu, 01 Jan 1970 00:00:00 GMT; path=/`;
    mock.verify();
  });
});

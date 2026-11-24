import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { NotificationService } from './notification.service';
import { rateLimitInterceptor } from './rate-limit.interceptor';

describe('rateLimitInterceptor', () => {
  let http: HttpClient;
  let mock: HttpTestingController;
  let notifications: NotificationService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withInterceptors([rateLimitInterceptor])), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpClient);
    mock = TestBed.inject(HttpTestingController);
    notifications = TestBed.inject(NotificationService);
  });

  afterEach(() => mock.verify());

  it('shows Retry-After on a 429 and rethrows the error', () => {
    let failed = false;
    http.get('/api/x').subscribe({ error: () => (failed = true) });
    mock.expectOne('/api/x').flush({}, { status: 429, statusText: 'Too Many', headers: { 'Retry-After': '42' } });

    expect(failed).toBeTrue();
    expect(notifications.message()).toContain('42 seconds');
  });

  it('falls back to 60 seconds without Retry-After', () => {
    http.get('/api/x').subscribe({ error: () => undefined });
    mock.expectOne('/api/x').flush({}, { status: 429, statusText: 'Too Many' });
    expect(notifications.message()).toContain('60 seconds');
  });

  it('reports a block on a 403', () => {
    http.get('/api/x').subscribe({ error: () => undefined });
    mock.expectOne('/api/x').flush('Forbidden', { status: 403, statusText: 'Forbidden' });
    expect(notifications.message()).toContain('blocked');
  });

  it('leaves other errors alone', () => {
    http.get('/api/x').subscribe({ error: () => undefined });
    mock.expectOne('/api/x').flush({}, { status: 500, statusText: 'Error' });
    expect(notifications.message()).toBeNull();
  });
});

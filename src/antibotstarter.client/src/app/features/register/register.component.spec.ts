import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ApiConfigService } from '../../core/api-config.service';
import { PASSWORD_PATTERN, RegisterComponent } from './register.component';

describe('RegisterComponent', () => {
  let fixture: ComponentFixture<RegisterComponent>;
  let component: RegisterComponent;
  let mock: HttpTestingController;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      imports: [RegisterComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    TestBed.inject(ApiConfigService).config.set({ captchaEnabled: false, turnstileSiteKey: '' });
    mock = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(RegisterComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    mock.expectOne((r) => r.url === '/api/forms/token' && r.params.get('purpose') === 'register').flush({ token: 'rt-1' });
    await fixture.whenStable();
  });

  afterEach(() => mock.verify());

  it('the password rule matches the server rule', () => {
    expect(PASSWORD_PATTERN.test('Short1!')).toBeFalse();
    expect(PASSWORD_PATTERN.test('nouppercase-12345!')).toBeFalse();
    expect(PASSWORD_PATTERN.test('NoDigits-LongEnough!')).toBeFalse();
    expect(PASSWORD_PATTERN.test('NoSymbols1234567')).toBeFalse();
    expect(PASSWORD_PATTERN.test('Correct-Long-2026!')).toBeTrue();
  });

  it('shows the generic server message after registering', () => {
    component.form.setValue({ email: 'new@example.com', password: 'Correct-Long-2026!', website: '' });
    component.submit();
    const req = mock.expectOne('/api/auth/register');
    expect(req.request.body.formToken).toBe('rt-1');
    req.flush({ message: 'If the details are valid, you will receive an email' }, { status: 202, statusText: 'Accepted' });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('you will receive an email');
  });

  it('sends whatever a bot types in the honeypot to the server (the server rejects it)', () => {
    component.form.setValue({ email: 'bot@example.com', password: 'Correct-Long-2026!', website: 'http://spam' });
    component.submit();
    const req = mock.expectOne('/api/auth/register');
    expect(req.request.body.website).toBe('http://spam');
    req.flush({ error: 'request_rejected', message: 'The request could not be processed.' }, { status: 400, statusText: 'Bad Request' });
    mock.expectOne((r) => r.url === '/api/forms/token').flush({ token: 'rt-2' });
    expect(component.error()).toContain('could not be processed');
  });

  it('shows the password validation details', () => {
    component.form.setValue({ email: 'x@example.com', password: 'Correct-Long-2026!', website: '' });
    component.submit();
    mock.expectOne('/api/auth/register').flush(
      { error: 'invalid_password', details: ['This password appears in known data breaches.'] },
      { status: 400, statusText: 'Bad Request' },
    );
    mock.expectOne((r) => r.url === '/api/forms/token').flush({ token: 'rt-3' });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('data breaches');
  });
});

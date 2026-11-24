import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AppComponent } from './app.component';
import { CRAWLER_TRAP_PATH } from './core/security.constants';

describe('AppComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [AppComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
  });

  it('includes the anti-crawler trap link, invisible and not indexable', () => {
    const fixture = TestBed.createComponent(AppComponent);
    fixture.detectChanges();
    const trap: HTMLAnchorElement = fixture.nativeElement.querySelector('[data-testid=trap]');

    expect(trap.getAttribute('href')).toBe(CRAWLER_TRAP_PATH);
    expect(trap.getAttribute('rel')).toContain('nofollow');
    expect(trap.getAttribute('aria-hidden')).toBe('true');
    expect(trap.getAttribute('tabindex')).toBe('-1');
    expect(trap.classList).toContain('trap-link');
  });

  it('the trap matches the path configured on the server', () => {
    expect(CRAWLER_TRAP_PATH).toBe('/_internal/archive');
  });

  it('shows the project name in the home link, with no logo', () => {
    const fixture = TestBed.createComponent(AppComponent);
    fixture.detectChanges();
    const brand: HTMLAnchorElement = fixture.nativeElement.querySelector('[data-testid="brand"]');
    expect(brand.getAttribute('href')).toBe('/');
    expect(brand.textContent?.trim()).toBe('AntiBotStarter');
    expect(brand.querySelector('img')).toBeNull();
  });
});

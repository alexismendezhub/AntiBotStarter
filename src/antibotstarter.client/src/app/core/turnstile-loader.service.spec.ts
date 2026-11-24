import { TestBed } from '@angular/core/testing';
import { TURNSTILE_SCRIPT, TurnstileLoaderService } from './turnstile-loader.service';

describe('TurnstileLoaderService', () => {
  afterEach(() => {
    document.querySelectorAll(`script[src="${TURNSTILE_SCRIPT}"]`).forEach((s) => s.remove());
    delete window.turnstile;
  });

  it('inserts the Cloudflare script only once', () => {
    const svc = TestBed.inject(TurnstileLoaderService);
    void svc.load().catch(() => undefined);
    void svc.load().catch(() => undefined);
    expect(document.querySelectorAll(`script[src="${TURNSTILE_SCRIPT}"]`).length).toBe(1);
  });

  it('uses window.turnstile when already loaded', async () => {
    const api = { render: () => 'id', reset: () => undefined, remove: () => undefined };
    window.turnstile = api;
    expect(await TestBed.inject(TurnstileLoaderService).load()).toBe(api);
    expect(document.querySelectorAll(`script[src="${TURNSTILE_SCRIPT}"]`).length).toBe(0);
  });
});

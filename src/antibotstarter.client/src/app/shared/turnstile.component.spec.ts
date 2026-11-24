import { TestBed } from '@angular/core/testing';
import { TurnstileLoaderService } from '../core/turnstile-loader.service';
import { FakeTurnstile, fakeTurnstileLoader } from '../testing/fake-turnstile';
import { TurnstileComponent } from './turnstile.component';

describe('TurnstileComponent', () => {
  let api: FakeTurnstile;

  async function create(loader: Partial<TurnstileLoaderService>) {
    TestBed.configureTestingModule({
      imports: [TurnstileComponent],
      providers: [{ provide: TurnstileLoaderService, useValue: loader }],
    });
    const fixture = TestBed.createComponent(TurnstileComponent);
    fixture.componentRef.setInput('siteKey', 'k-123');
    fixture.componentRef.setInput('action', 'register');
    const emitted: (string | null)[] = [];
    fixture.componentInstance.token.subscribe((t) => emitted.push(t));
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return { fixture, emitted };
  }

  it('emits the solved token and null when it expires', async () => {
    api = new FakeTurnstile();
    const { emitted } = await create(fakeTurnstileLoader(api));
    expect(api.lastOptions?.sitekey).toBe('k-123');
    api.solve('tok');
    api.expire();
    expect(emitted).toEqual(['tok', null]);
  });

  it('reset() resets the widget and emits null', async () => {
    api = new FakeTurnstile();
    const { fixture, emitted } = await create(fakeTurnstileLoader(api));
    fixture.componentInstance.reset();
    expect(api.resets).toBe(1);
    expect(emitted).toEqual([null]);
  });

  it('removes the widget on destroy', async () => {
    api = new FakeTurnstile();
    const { fixture } = await create(fakeTurnstileLoader(api));
    fixture.destroy();
    expect(api.removed).toEqual(['widget-1']);
  });

  it('shows an error when the script fails to load', async () => {
    const { fixture } = await create({ load: () => Promise.reject(new Error('blocked')) });
    expect(fixture.nativeElement.textContent).toContain('could not be loaded');
  });
});

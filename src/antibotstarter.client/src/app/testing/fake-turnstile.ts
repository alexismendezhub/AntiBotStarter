import { TurnstileApi, TurnstileLoaderService, TurnstileRenderOptions } from '../core/turnstile-loader.service';

/** Fake Turnstile for tests: "solve" the challenge by calling solve(). */
export class FakeTurnstile implements TurnstileApi {
  lastOptions?: TurnstileRenderOptions;
  resets = 0;
  removed: string[] = [];

  render(_container: HTMLElement, options: TurnstileRenderOptions): string {
    this.lastOptions = options;
    return 'widget-1';
  }

  reset(): void {
    this.resets++;
  }

  remove(id: string): void {
    this.removed.push(id);
  }

  solve(token: string): void {
    this.lastOptions?.callback(token);
  }

  expire(): void {
    this.lastOptions?.['expired-callback']();
  }
}

export function fakeTurnstileLoader(api: FakeTurnstile): Partial<TurnstileLoaderService> {
  return { load: () => Promise.resolve(api) };
}

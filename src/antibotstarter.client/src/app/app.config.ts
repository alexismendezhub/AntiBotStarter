import { provideHttpClient, withInterceptors, withXsrfConfiguration } from '@angular/common/http';
import { ApplicationConfig, inject, provideAppInitializer, provideBrowserGlobalErrorListeners, provideZoneChangeDetection } from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { routes } from './app.routes';
import { ApiConfigService } from './core/api-config.service';
import { rateLimitInterceptor } from './core/rate-limit.interceptor';
import { XSRF_COOKIE, XSRF_HEADER } from './core/security.constants';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideZoneChangeDetection({ eventCoalescing: true }),
    // Route data (e.g. { kind: 'forgot' }) is bound to component inputs.
    provideRouter(routes, withComponentInputBinding()),
    provideHttpClient(
      // HttpClient copies the XSRF-TOKEN cookie into the X-XSRF-TOKEN header on relative POST/PUT/DELETE
      withXsrfConfiguration({ cookieName: XSRF_COOKIE, headerName: XSRF_HEADER }),
      withInterceptors([rateLimitInterceptor]),
    ),
    provideAppInitializer(() => inject(ApiConfigService).init()),
  ],
};

import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { NotificationService } from './notification.service';

/** Turns 429 (rate limit) and 403 (anti-bot block) into user-facing messages. */
export const rateLimitInterceptor: HttpInterceptorFn = (req, next) => {
  const notifications = inject(NotificationService);
  return next(req).pipe(
    catchError((err: unknown) => {
      if (err instanceof HttpErrorResponse) {
        if (err.status === 429) {
          const seconds = Number(err.headers.get('Retry-After')) || 60;
          notifications.show(`Too many requests. Wait ${seconds} seconds and try again.`);
        } else if (err.status === 403) {
          notifications.show('Your request was blocked by the site protections.');
        }
      }
      return throwError(() => err);
    }),
  );
};

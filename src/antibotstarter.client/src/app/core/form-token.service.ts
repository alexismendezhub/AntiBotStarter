import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';

export type FormPurpose = 'login' | 'register' | 'resend' | 'forgot' | 'reset';

/** Server-signed token: measures fill-in time and is single-use. */
@Injectable({ providedIn: 'root' })
export class FormTokenService {
  private readonly http = inject(HttpClient);

  get(purpose: FormPurpose): Observable<string> {
    return this.http
      .get<{ token: string }>('/api/forms/token', { params: { purpose } })
      .pipe(map((r) => r.token));
  }
}

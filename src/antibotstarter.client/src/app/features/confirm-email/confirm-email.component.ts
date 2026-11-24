import { Component, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth.service';

@Component({
  selector: 'app-confirm-email',
  imports: [RouterLink],
  template: `
    <h1>Confirm email</h1>
    @switch (state()) {
      @case ('working') { <p>Confirming…</p> }
      @case ('ok') { <p class="ok">Email confirmed. <a routerLink="/login">Sign in</a>.</p> }
      @case ('error') { <p class="error">The link is invalid or has expired.</p> }
    }
  `,
})
export class ConfirmEmailComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly auth = inject(AuthService);

  readonly state = signal<'working' | 'ok' | 'error'>('working');

  ngOnInit(): void {
    const q = this.route.snapshot.queryParamMap;
    const userId = q.get('userId');
    const code = q.get('code');
    if (!userId || !code) {
      this.state.set('error');
      return;
    }
    this.auth.confirmEmail(userId, code).subscribe({
      next: () => this.state.set('ok'),
      error: () => this.state.set('error'),
    });
  }
}

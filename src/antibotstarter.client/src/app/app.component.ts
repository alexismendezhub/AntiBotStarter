import { Component, inject } from '@angular/core';
import { Router, RouterLink, RouterOutlet } from '@angular/router';
import { AuthService } from './core/auth.service';
import { NotificationService } from './core/notification.service';
import { CRAWLER_TRAP_PATH } from './core/security.constants';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink],
  template: `
    <nav>
      <a routerLink="/" class="brand" data-testid="brand">AntiBotStarter</a>
      @if (auth.currentUser(); as user) {
        <a routerLink="/products">Products</a>
        <a routerLink="/account/security">Security</a>
        <span>{{ user }}</span>
        <button type="button" (click)="logout()">Sign out</button>
      } @else {
        <a routerLink="/login">Sign in</a>
        <a routerLink="/register">Register</a>
      }
    </nav>

    <main>
      @if (notifications.message(); as msg) {
        <p class="notice" role="status">{{ msg }} <button type="button" (click)="notifications.clear()">×</button></p>
      }
      <router-outlet />
    </main>

    <!-- Anti-crawler trap for bots that run JavaScript. Plain href (not routerLink)
         so the crawler makes a real HTTP request that the server detects. -->
    <a class="trap-link" [attr.href]="trapPath" rel="nofollow noopener" tabindex="-1" aria-hidden="true" data-testid="trap">archive</a>
  `,
})
export class AppComponent {
  protected readonly auth = inject(AuthService);
  protected readonly notifications = inject(NotificationService);
  private readonly router = inject(Router);

  protected readonly trapPath = CRAWLER_TRAP_PATH;

  logout(): void {
    this.auth.logout().subscribe(() => void this.router.navigate(['/']));
  }
}

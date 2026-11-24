import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-home',
  imports: [RouterLink],
  template: `
    <h1>AntiBotStarter</h1>
    <p>ASP.NET Core + Angular starter with layered anti-bot and anti-crawler protection.</p>
    <p><a routerLink="/register">Create account</a> · <a routerLink="/login">Sign in</a></p>
  `,
})
export class HomeComponent {}

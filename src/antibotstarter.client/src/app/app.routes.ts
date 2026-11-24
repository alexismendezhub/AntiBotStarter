import { Routes } from '@angular/router';
import { authGuard } from './core/auth.guard';
import { AccountSecurityComponent } from './features/account/account-security.component';
import { EmailRequestComponent } from './features/account/email-request.component';
import { ResetPasswordComponent } from './features/account/reset-password.component';
import { ConfirmEmailComponent } from './features/confirm-email/confirm-email.component';
import { HomeComponent } from './features/home/home.component';
import { LoginComponent } from './features/login/login.component';
import { ProductsComponent } from './features/products/products.component';
import { RegisterComponent } from './features/register/register.component';

export const routes: Routes = [
  { path: '', component: HomeComponent, title: 'AntiBotStarter' },
  { path: 'login', component: LoginComponent, title: 'Sign in' },
  { path: 'register', component: RegisterComponent, title: 'Create account' },
  { path: 'confirm-email', component: ConfirmEmailComponent, title: 'Confirm email' },
  { path: 'forgot-password', component: EmailRequestComponent, data: { kind: 'forgot' }, title: 'Reset your password' },
  { path: 'resend-confirmation', component: EmailRequestComponent, data: { kind: 'resend' }, title: 'Resend confirmation' },
  { path: 'reset-password', component: ResetPasswordComponent, title: 'Choose a new password' },
  { path: 'account/security', component: AccountSecurityComponent, canActivate: [authGuard], title: 'Account security' },
  { path: 'products', component: ProductsComponent, canActivate: [authGuard], title: 'Products' },
  { path: '**', redirectTo: '' },
];

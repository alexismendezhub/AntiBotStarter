import { CurrencyPipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';

export interface Product {
  id: string;
  name: string;
  price: number;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  hasMore: boolean;
}

export const PAGE_SIZE = 20;

/** Paged list: the server limits page size, max page and per-user rate. */
@Component({
  selector: 'app-products',
  imports: [CurrencyPipe],
  template: `
    <h1>Products</h1>
    @if (error()) {
      <p class="error">{{ error() }}</p>
    }
    <table>
      <thead><tr><th>Name</th><th>Price</th></tr></thead>
      <tbody>
        @for (p of items(); track p.id) {
          <tr><td>{{ p.name }}</td><td>{{ p.price | currency: 'USD' }}</td></tr>
        }
      </tbody>
    </table>
    <p>
      <button type="button" (click)="go(page() - 1)" [disabled]="page() <= 1 || loading()">Previous</button>
      Page {{ page() }}
      <button type="button" (click)="go(page() + 1)" [disabled]="!hasMore() || loading()">Next</button>
    </p>
  `,
})
export class ProductsComponent implements OnInit {
  private readonly http = inject(HttpClient);

  readonly items = signal<Product[]>([]);
  readonly page = signal(1);
  readonly hasMore = signal(false);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  ngOnInit(): void {
    this.go(1);
  }

  go(page: number): void {
    if (page < 1) return;
    this.loading.set(true);
    this.error.set(null);
    this.http
      .get<PagedResult<Product>>('/api/products', { params: { page, pageSize: PAGE_SIZE } })
      .subscribe({
        next: (r) => {
          this.items.set(r.items);
          this.page.set(r.page);
          this.hasMore.set(r.hasMore);
          this.loading.set(false);
        },
        error: () => {
          this.error.set('The products could not be loaded.');
          this.loading.set(false);
        },
      });
  }
}

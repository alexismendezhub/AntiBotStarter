import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { PAGE_SIZE, ProductsComponent } from './products.component';

describe('ProductsComponent', () => {
  it('requests pages within the server limit and honors hasMore', () => {
    TestBed.configureTestingModule({
      imports: [ProductsComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    const mock = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ProductsComponent);
    fixture.detectChanges();

    const req = mock.expectOne((r) => r.url === '/api/products');
    expect(PAGE_SIZE).toBeLessThanOrEqual(50);
    expect(req.request.params.get('pageSize')).toBe(String(PAGE_SIZE));
    expect(req.request.params.get('page')).toBe('1');
    req.flush({ items: [{ id: 'a1', name: 'Product 001', price: 10 }], page: 1, pageSize: PAGE_SIZE, hasMore: false });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Product 001');
    const next: HTMLButtonElement = fixture.nativeElement.querySelectorAll('button')[1];
    expect(next.disabled).toBeTrue();
    mock.verify();
  });
});

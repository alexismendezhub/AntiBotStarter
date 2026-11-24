using AntiBotStarter.Application.Configuration;
using AntiBotStarter.Domain.Products;
using AntiBotStarter.Infrastructure.Products;
using AntiBotStarter.Server.Security;
using Microsoft.Extensions.Options;

namespace AntiBotStarter.Server.Endpoints;

/// <summary>
/// Data API anti-scraping:
///  - Requires a session (configurable) → the scraper needs an email-confirmed account.
///  - Token bucket per user/IP.
///  - Bounded page size and max page → the full catalog cannot be downloaded.
///  - The total item count is not exposed (only HasMore).
/// </summary>
public static class ProductEndpoints
{
    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder app, AntiBotOptions options)
    {
        var group = app.MapGroup("/api/products").RequireRateLimiting(RateLimitPolicies.Api);
        if (options.Scraping.RequireAuthForData) group.RequireAuthorization();

        group.MapGet("/", (int? page, int? pageSize, ProductStore store, IOptionsMonitor<AntiBotOptions> o) =>
        {
            var s = o.CurrentValue.Scraping;
            var p = page ?? 1;
            var size = pageSize ?? 20;
            if (p < 1 || p > s.MaxPage || size < 1 || size > s.MaxPageSize)
            {
                return Results.Json(new { error = "invalid_paging", maxPage = s.MaxPage, maxPageSize = s.MaxPageSize },
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var items = store.Page(p, size);
            var hasMore = p < s.MaxPage && p * size < store.Count;
            return Results.Ok(new PagedResult<ProductDto>(items, p, size, hasMore));
        });

        group.MapGet("/{id:guid}", (Guid id, ProductStore store) =>
            store.Find(id) is { } product ? Results.Ok(product) : Results.NotFound());

        return app;
    }
}

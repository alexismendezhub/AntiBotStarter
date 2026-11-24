using System.Net;
using AntiBotStarter.Domain.Products;
using AntiBotStarter.Server.IntegrationTests.Support;

namespace AntiBotStarter.Server.IntegrationTests;

public sealed class ProductsTests
{
    [Fact]
    public async Task Anonymous_Gets401()
    {
        using var f = new AntiBotFactory();
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.CreateBrowserClient().GetAsync("/api/products")).StatusCode);
    }

    [Fact]
    public async Task Authenticated_GetsPagedGuids_WithoutTotalCount()
    {
        using var f = new AntiBotFactory();
        var c = f.CreateBrowserClient();
        await c.RegisterAndConfirmAsync(f, "reader@example.com");
        await c.LoginAsync("reader@example.com", ClientExtensions.StrongPassword);

        var res = await c.GetAsync("/api/products?page=1&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.DoesNotContain("total", body, StringComparison.OrdinalIgnoreCase);

        var page = await res.Content.ReadFromJsonAsync<PagedResult<ProductDto>>();
        Assert.Equal(10, page!.Items.Count);
        Assert.True(page.HasMore);
        Assert.All(page.Items, p => Assert.NotEqual(Guid.Empty, p.Id));
    }

    [Theory]
    [InlineData("/api/products?pageSize=51")]
    [InlineData("/api/products?pageSize=0")]
    [InlineData("/api/products?page=21")]
    [InlineData("/api/products?page=0")]
    public async Task PagingOutsideLimits_Returns400(string url)
    {
        using var f = new AntiBotFactory();
        var c = f.CreateBrowserClient();
        await c.RegisterAndConfirmAsync(f, "pag@example.com");
        await c.LoginAsync("pag@example.com", ClientExtensions.StrongPassword);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task LastAllowedPage_ReportsNoMore()
    {
        using var f = new AntiBotFactory();
        var c = f.CreateBrowserClient();
        await c.RegisterAndConfirmAsync(f, "last@example.com");
        await c.LoginAsync("last@example.com", ClientExtensions.StrongPassword);
        var page = await c.GetFromJsonAsync<PagedResult<ProductDto>>("/api/products?page=20&pageSize=10");
        Assert.False(page!.HasMore);
    }

    [Fact]
    public async Task DataApi_IsRateLimitedPerUser()
    {
        using var f = new AntiBotFactory(new Dictionary<string, string?>
        {
            ["AntiBot:RateLimits:ApiTokenLimit"] = "3",
            ["AntiBot:RateLimits:ApiTokensPerPeriod"] = "1",
            ["AntiBot:RateLimits:ApiReplenishSeconds"] = "3600",
        });
        var c = f.CreateBrowserClient();
        // Registration and login consume tokens from the per-IP bucket; afterwards the API counts per user.
        await c.RegisterAndConfirmAsync(f, "limit@example.com");
        await c.LoginAsync("limit@example.com", ClientExtensions.StrongPassword);

        for (var i = 0; i < 3; i++) Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/products")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await c.GetAsync("/api/products")).StatusCode);
    }

    [Fact]
    public async Task PublicData_WhenAuthNotRequired()
    {
        using var f = new AntiBotFactory(new Dictionary<string, string?> { ["AntiBot:Scraping:RequireAuthForData"] = "false" });
        Assert.Equal(HttpStatusCode.OK, (await f.CreateBrowserClient().GetAsync("/api/products")).StatusCode);
    }
}

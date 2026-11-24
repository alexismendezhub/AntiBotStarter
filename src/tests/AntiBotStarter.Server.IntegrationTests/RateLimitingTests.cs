using System.Net;
using AntiBotStarter.Server.IntegrationTests.Support;

namespace AntiBotStarter.Server.IntegrationTests;

public sealed class RateLimitingTests
{
    [Fact]
    public async Task GlobalLimit_Returns429_WithRetryAfter()
    {
        using var f = new AntiBotFactory(new Dictionary<string, string?> { ["AntiBot:RateLimits:GlobalPermitPerMinute"] = "3" });
        var c = f.CreateBrowserClient(ip: "203.0.113.30");

        for (var i = 0; i < 3; i++) Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/config")).StatusCode);

        var blocked = await c.GetAsync("/api/config");
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.True(blocked.Headers.RetryAfter is not null);
        Assert.Contains("rate_limited", await blocked.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AuthPolicy_IsPerIp()
    {
        using var f = new AntiBotFactory(new Dictionary<string, string?> { ["AntiBot:RateLimits:AuthPermitPerMinute"] = "5" });
        var attacker = f.CreateBrowserClient(ip: "203.0.113.31");

        // The first 5 fail antiforgery (400) but still count towards the limit
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.BadRequest, (await attacker.PostAsJsonAsync("/api/auth/login", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await attacker.PostAsJsonAsync("/api/auth/login", new { })).StatusCode);

        var other = f.CreateBrowserClient(ip: "203.0.113.32");
        Assert.Equal(HttpStatusCode.BadRequest, (await other.PostAsJsonAsync("/api/auth/login", new { })).StatusCode);
    }

    [Fact]
    public async Task SpoofedXForwardedFor_FromUnknownProxy_DoesNotBypassLimit()
    {
        using var f = new AntiBotFactory(new Dictionary<string, string?> { ["AntiBot:RateLimits:AuthPermitPerMinute"] = "2" });
        var attacker = f.CreateBrowserClient(ip: "203.0.113.33");

        for (var i = 0; i < 2; i++) await attacker.PostAsJsonAsync("/api/auth/login", new { });

        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login") { Content = JsonContent.Create(new { }) };
        req.Headers.Add("X-Forwarded-For", "198.18.0.99"); // tries to impersonate another IP
        Assert.Equal(HttpStatusCode.TooManyRequests, (await attacker.SendAsync(req)).StatusCode);
    }

    [Fact]
    public async Task XForwardedFor_FromKnownProxy_IsHonored()
    {
        using var f = new AntiBotFactory(new Dictionary<string, string?>
        {
            ["AntiBot:RateLimits:AuthPermitPerMinute"] = "1",
            ["AntiBot:ForwardedHeaders:KnownProxies:0"] = "10.0.0.5",
        });
        var viaProxy = f.CreateBrowserClient(ip: "10.0.0.5");

        async Task<HttpStatusCode> SendAs(string clientIp)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login") { Content = JsonContent.Create(new { }) };
            req.Headers.Add("X-Forwarded-For", clientIp);
            return (await viaProxy.SendAsync(req)).StatusCode;
        }

        Assert.Equal(HttpStatusCode.BadRequest, await SendAs("198.18.0.1"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await SendAs("198.18.0.1"));
        // Another real client behind the same proxy has its own quota
        Assert.Equal(HttpStatusCode.BadRequest, await SendAs("198.18.0.2"));
    }

    [Fact]
    public async Task Health_IsNotRateLimited()
    {
        using var f = new AntiBotFactory(new Dictionary<string, string?> { ["AntiBot:RateLimits:GlobalPermitPerMinute"] = "2" });
        var c = f.CreateBrowserClient(ip: "203.0.113.34");
        for (var i = 0; i < 6; i++) Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/health")).StatusCode);
    }

    [Fact]
    public async Task FormTokenEndpoint_UsesApiTokenBucket()
    {
        using var f = new AntiBotFactory(new Dictionary<string, string?>
        {
            ["AntiBot:RateLimits:ApiTokenLimit"] = "2",
            ["AntiBot:RateLimits:ApiTokensPerPeriod"] = "1",
            ["AntiBot:RateLimits:ApiReplenishSeconds"] = "3600",
        });
        var c = f.CreateBrowserClient(ip: "203.0.113.35");
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/forms/token?purpose=login")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/forms/token?purpose=login")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await c.GetAsync("/api/forms/token?purpose=login")).StatusCode);
    }
}

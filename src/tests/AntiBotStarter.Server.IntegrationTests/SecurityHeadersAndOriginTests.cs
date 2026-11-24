using System.Net;
using AntiBotStarter.Server.IntegrationTests.Support;

namespace AntiBotStarter.Server.IntegrationTests;

public sealed class SecurityHeadersAndOriginTests
{
    [Fact]
    public async Task ApiResponses_HaveSecurityHeaders()
    {
        using var f = new AntiBotFactory();
        var res = await f.CreateBrowserClient().GetAsync("/api/config");

        Assert.Equal("nosniff", res.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", res.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("frame-ancestors 'none'", res.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Contains("https://challenges.cloudflare.com", res.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Contains("noindex", res.Headers.GetValues("X-Robots-Tag").Single());
        Assert.True(res.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task RejectedRequests_AlsoHaveSecurityHeaders()
    {
        using var f = new AntiBotFactory();
        var res = await f.CreateBrowserClient(userAgent: "python-requests/2.32").GetAsync("/api/config");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Equal("nosniff", res.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task OriginLock_RejectsRequestsWithoutFrontDoorHeader()
    {
        using var f = new AntiBotFactory(new Dictionary<string, string?>
        {
            ["AntiBot:OriginLock:Enabled"] = "true",
            ["AntiBot:OriginLock:ExpectedValues:0"] = "11111111-2222-3333-4444-555555555555",
        });
        var direct = f.CreateBrowserClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await direct.GetAsync("/api/config")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await direct.GetAsync("/health")).StatusCode);

        var wrong = f.CreateBrowserClient();
        wrong.DefaultRequestHeaders.Add("X-Azure-FDID", "other-front-door");
        Assert.Equal(HttpStatusCode.Forbidden, (await wrong.GetAsync("/api/config")).StatusCode);

        var viaFrontDoor = f.CreateBrowserClient();
        viaFrontDoor.DefaultRequestHeaders.Add("X-Azure-FDID", "11111111-2222-3333-4444-555555555555");
        Assert.Equal(HttpStatusCode.OK, (await viaFrontDoor.GetAsync("/api/config")).StatusCode);
    }

    [Fact]
    public async Task UnknownApiRoute_Returns404_NotIndexHtml()
    {
        using var f = new AntiBotFactory();
        Assert.Equal(HttpStatusCode.NotFound, (await f.CreateBrowserClient().GetAsync("/api/does-not-exist")).StatusCode);
    }

    [Fact]
    public async Task PublicConfig_NeverExposesSecret()
    {
        using var f = new AntiBotFactory(new Dictionary<string, string?>
        {
            ["AntiBot:Captcha:SiteKey"] = "public-site-key",
            ["AntiBot:Captcha:SecretKey"] = "secret-that-must-not-leak",
        });
        var body = await f.CreateBrowserClient().GetStringAsync("/api/config");
        Assert.Contains("public-site-key", body);
        Assert.DoesNotContain("secret-that-must-not-leak", body);
    }
}

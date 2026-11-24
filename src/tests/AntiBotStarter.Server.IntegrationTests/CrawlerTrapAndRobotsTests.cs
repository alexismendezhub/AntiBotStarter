using System.Net;
using AntiBotStarter.Server.IntegrationTests.Support;

namespace AntiBotStarter.Server.IntegrationTests;

public sealed class CrawlerTrapAndRobotsTests
{
    [Fact]
    public async Task HittingTrap_BlocksThatIp_ButNotOthers()
    {
        using var f = new AntiBotFactory();
        var crawler = f.CreateBrowserClient(ip: "203.0.113.20");
        var human = f.CreateBrowserClient(ip: "203.0.113.21");

        Assert.Equal(HttpStatusCode.OK, (await crawler.GetAsync("/api/config")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await crawler.GetAsync("/_internal/archive/page-1")).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await crawler.GetAsync("/api/config")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await human.GetAsync("/api/config")).StatusCode);
    }

    [Fact]
    public async Task BlockedIp_CanStillReachHealth()
    {
        using var f = new AntiBotFactory();
        var crawler = f.CreateBrowserClient(ip: "203.0.113.22");
        await crawler.GetAsync("/_internal/archive");
        Assert.Equal(HttpStatusCode.OK, (await crawler.GetAsync("/health")).StatusCode);
    }

    [Fact]
    public async Task IPv6_IsBlockedPerSlash64()
    {
        using var f = new AntiBotFactory();
        await f.CreateBrowserClient(ip: "2001:db8:abcd:12::1").GetAsync("/_internal/archive");
        // Another address in the same /64 is blocked too (cannot rotate addresses to evade)
        var res = await f.CreateBrowserClient(ip: "2001:db8:abcd:12::ffff").GetAsync("/api/config");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task RobotsTxt_DisallowsEverything_ByDefault_AndListsAiCrawlers()
    {
        using var f = new AntiBotFactory();
        var res = await f.CreateBrowserClient().GetAsync("/robots.txt");
        var body = await res.Content.ReadAsStringAsync();

        Assert.StartsWith("text/plain", res.Content.Headers.ContentType?.MediaType);
        Assert.Contains("User-agent: GPTBot", body);
        Assert.Contains("User-agent: Google-Extended", body);
        Assert.Contains("User-agent: *\nDisallow: /\n", body.Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task RobotsTxt_WhenSearchEnginesAllowed_DisallowsApiAndTrapOnly()
    {
        using var f = new AntiBotFactory(new Dictionary<string, string?> { ["AntiBot:AllowSearchEngines"] = "true" });
        var body = (await f.CreateBrowserClient().GetStringAsync("/robots.txt")).Replace("\r\n", "\n");

        var generalSection = body[body.IndexOf("User-agent: *", StringComparison.Ordinal)..];
        Assert.Contains("Disallow: /api/", generalSection);
        Assert.Contains("Disallow: /_internal/archive", generalSection);
        Assert.DoesNotContain("Disallow: /\n", generalSection);
    }

    [Fact]
    public async Task NoIndexHeader_IsSent_WhenSearchEnginesNotAllowed()
    {
        using var f = new AntiBotFactory();
        var res = await f.CreateBrowserClient().GetAsync("/robots.txt");
        Assert.Contains("noindex", res.Headers.GetValues("X-Robots-Tag").Single());
    }
}

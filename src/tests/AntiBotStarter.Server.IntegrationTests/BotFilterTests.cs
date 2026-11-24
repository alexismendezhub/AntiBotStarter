using System.Net;
using AntiBotStarter.Server.IntegrationTests.Support;

namespace AntiBotStarter.Server.IntegrationTests;

public sealed class BotFilterTests
{
    private const string GooglebotUa = "Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)";

    [Fact]
    public async Task NormalBrowser_IsAllowed()
    {
        using var f = new AntiBotFactory();
        var res = await f.CreateBrowserClient().GetAsync("/api/config");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task EmptyUserAgent_IsBlocked()
    {
        using var f = new AntiBotFactory();
        var res = await f.CreateBrowserClient(userAgent: null).GetAsync("/api/config");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Theory]
    [InlineData("python-requests/2.32.3")]
    [InlineData("curl/8.9.1")]
    [InlineData("Scrapy/2.11 (+https://scrapy.org)")]
    [InlineData("Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 HeadlessChrome/130.0 Safari/537.36")]
    [InlineData("Mozilla/5.0 (compatible; AhrefsBot/7.0; +http://ahrefs.com/robot/)")]
    public async Task ScraperUserAgents_AreBlocked(string ua)
    {
        using var f = new AntiBotFactory();
        var res = await f.CreateBrowserClient(userAgent: ua).GetAsync("/api/config");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Theory]
    [InlineData("Mozilla/5.0 AppleWebKit/537.36 (KHTML, like Gecko; compatible; GPTBot/1.2; +https://openai.com/gptbot)")]
    [InlineData("Mozilla/5.0 AppleWebKit/537.36 (KHTML, like Gecko; compatible; ClaudeBot/1.0; +claudebot@anthropic.com)")]
    [InlineData("CCBot/2.0 (https://commoncrawl.org/faq/)")]
    [InlineData("Mozilla/5.0 (compatible; Bytespider; spider-feedback@bytedance.com)")]
    public async Task AiCrawlers_AreBlocked(string ua)
    {
        using var f = new AntiBotFactory();
        var res = await f.CreateBrowserClient(userAgent: ua).GetAsync("/");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Googlebot_IsBlocked_WhenSearchEnginesNotAllowed()
    {
        using var f = new AntiBotFactory();
        var res = await f.CreateBrowserClient(ip: "66.249.66.1", userAgent: GooglebotUa).GetAsync("/");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task SpoofedGooglebot_IsBlocked_WhenDnsDoesNotVerify()
    {
        using var f = new AntiBotFactory(new Dictionary<string, string?> { ["AntiBot:AllowSearchEngines"] = "true" });
        var res = await f.CreateBrowserClient(ip: "203.0.113.50", userAgent: GooglebotUa).GetAsync("/");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task VerifiedGooglebot_IsAllowed_WhenSearchEnginesAllowed()
    {
        using var f = new AntiBotFactory(new Dictionary<string, string?> { ["AntiBot:AllowSearchEngines"] = "true" });
        f.Dns.Reverse["66.249.66.1"] = "crawl-66-249-66-1.googlebot.com.";
        f.Dns.Forward["crawl-66-249-66-1.googlebot.com"] = [IPAddress.Parse("66.249.66.1")];

        var res = await f.CreateBrowserClient(ip: "66.249.66.1", userAgent: GooglebotUa).GetAsync("/api/config");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task LookalikeDomain_IsRejected()
    {
        using var f = new AntiBotFactory(new Dictionary<string, string?> { ["AntiBot:AllowSearchEngines"] = "true" });
        f.Dns.Reverse["203.0.113.7"] = "crawl.fakegooglebot.com";
        f.Dns.Forward["crawl.fakegooglebot.com"] = [IPAddress.Parse("203.0.113.7")];

        var res = await f.CreateBrowserClient(ip: "203.0.113.7", userAgent: GooglebotUa).GetAsync("/api/config");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task ReverseDnsWithoutMatchingForward_IsRejected()
    {
        using var f = new AntiBotFactory(new Dictionary<string, string?> { ["AntiBot:AllowSearchEngines"] = "true" });
        f.Dns.Reverse["203.0.113.8"] = "crawl-1.googlebot.com"; // the attacker controls its PTR…
        f.Dns.Forward["crawl-1.googlebot.com"] = [IPAddress.Parse("66.249.66.1")]; // …but not Google's DNS

        var res = await f.CreateBrowserClient(ip: "203.0.113.8", userAgent: GooglebotUa).GetAsync("/api/config");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task RobotsTxt_And_Health_AreReachable_ByAnyUserAgent()
    {
        using var f = new AntiBotFactory();
        var bot = f.CreateBrowserClient(userAgent: GooglebotUa);
        Assert.Equal(HttpStatusCode.OK, (await bot.GetAsync("/robots.txt")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await f.CreateBrowserClient(userAgent: null).GetAsync("/health")).StatusCode);
    }
}

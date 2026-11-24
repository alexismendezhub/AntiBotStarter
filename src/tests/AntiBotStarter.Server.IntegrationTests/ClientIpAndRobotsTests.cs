using System.Net;
using AntiBotStarter.Application.Configuration;
using AntiBotStarter.Application.Security;
using AntiBotStarter.Server.Security;

namespace AntiBotStarter.Server.IntegrationTests;

public sealed class ClientIpAndRobotsTests
{
    [Theory]
    [InlineData("203.0.113.5", "203.0.113.5")]
    [InlineData("::ffff:203.0.113.5", "203.0.113.5")]
    [InlineData("2001:db8:1:2:aaaa:bbbb:cccc:dddd", "2001:db8:1:2::/64")]
    [InlineData("::1", "::1")]
    public void Key_NormalizesAddresses(string input, string expected) =>
        Assert.Equal(expected, ClientIp.Key(IPAddress.Parse(input)));

    [Fact]
    public void Key_NullAddress_IsUnknown() => Assert.Equal("unknown", ClientIp.Key((IPAddress?)null));

    [Fact]
    public void Robots_IncludesConfiguredTrapPath_WhenSearchEnginesAllowed()
    {
        var o = new AntiBotOptions
        {
            AllowSearchEngines = true,
            CrawlerTrap = new CrawlerTrapSettings { Enabled = true, Path = "/trap" },
            BotFilter = new BotFilterSettings { AiCrawlerUserAgents = ["GPTBot", "gptbot"], RobotsOnlyAiTokens = ["Google-Extended"] }
        };
        var txt = RobotsTxtBuilder.Build(o);
        Assert.Contains("Disallow: /trap", txt);
        Assert.Single(txt.Split('\n'), l => l.Trim().Equals("User-agent: GPTBot", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("User-agent: Google-Extended", txt);
    }
}

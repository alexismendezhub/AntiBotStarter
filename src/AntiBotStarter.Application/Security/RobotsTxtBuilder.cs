using System.Text;
using AntiBotStarter.Application.Configuration;

namespace AntiBotStarter.Application.Security;

/// <summary>
/// robots.txt generated from configuration. Only "polite" bots honor it, but it is what makes
/// the trap work: whoever enters a Disallowed path is ignoring it.
/// </summary>
public static class RobotsTxtBuilder
{
    public static string Build(AntiBotOptions o)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# AntiBotStarter - generated from the AntiBot configuration");
        sb.AppendLine("# AI / model-training crawlers: no access");

        foreach (var token in o.BotFilter.AiCrawlerUserAgents.Concat(o.BotFilter.RobotsOnlyAiTokens)
                     .Where(t => !string.IsNullOrWhiteSpace(t))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            sb.Append("User-agent: ").AppendLine(token);
        }
        sb.AppendLine("Disallow: /");
        sb.AppendLine();

        sb.AppendLine("User-agent: *");
        if (o.AllowSearchEngines)
        {
            sb.AppendLine("Disallow: /api/");
            if (o.CrawlerTrap.Enabled) sb.Append("Disallow: ").AppendLine(o.CrawlerTrap.Path);
        }
        else
        {
            sb.AppendLine("Disallow: /");
        }

        return sb.ToString();
    }
}

using AntiBotStarter.Application.Configuration;
using AntiBotStarter.Application.Security;
using AntiBotStarter.Server.Security;
using Microsoft.Extensions.Options;

namespace AntiBotStarter.Server.Middleware;

/// <summary>
/// User-Agent filter. A MINOR layer (UAs are trivial to fake), but it removes the noise of
/// simple scrapers, AI crawlers and fake search engines.
/// Order: empty UA → AI crawlers → search engines (DNS-verified) → scraping tools.
/// </summary>
public sealed class BotFilterMiddleware(
    RequestDelegate next,
    IOptionsMonitor<AntiBotOptions> options,
    SearchEngineVerifier verifier,
    SecurityEvents events)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        var o = options.CurrentValue;
        var f = o.BotFilter;
        if (!f.Enabled || PathRules.IsUnder(ctx.Request.Path, o.ExemptPaths))
        {
            await next(ctx);
            return;
        }

        var ua = ctx.Request.Headers.UserAgent.ToString();

        if (string.IsNullOrWhiteSpace(ua))
        {
            if (f.BlockEmptyUserAgent)
            {
                await RejectAsync(ctx, "empty_user_agent");
                return;
            }
            await next(ctx);
            return;
        }

        if (f.BlockAiCrawlers && ContainsAny(ua, f.AiCrawlerUserAgents))
        {
            await RejectAsync(ctx, "ai_crawler");
            return;
        }

        var engine = f.SearchEngines.FirstOrDefault(e =>
            !string.IsNullOrEmpty(e.UserAgentFragment) &&
            ua.Contains(e.UserAgentFragment, StringComparison.OrdinalIgnoreCase));

        if (engine is not null)
        {
            if (!o.AllowSearchEngines)
            {
                await RejectAsync(ctx, "search_engine_not_allowed");
                return;
            }

            var ip = ctx.Connection.RemoteIpAddress;
            if (ip is null || !await verifier.IsVerifiedAsync(ip, engine.AllowedHostSuffixes, ctx.RequestAborted))
            {
                await RejectAsync(ctx, "spoofed_search_engine");
                return;
            }

            await next(ctx);
            return;
        }

        if (ContainsAny(ua, f.BlockedUserAgentFragments))
        {
            await RejectAsync(ctx, "blocked_user_agent");
            return;
        }

        await next(ctx);
    }

    private static bool ContainsAny(string ua, IEnumerable<string> fragments) =>
        fragments.Any(fr => !string.IsNullOrEmpty(fr) && ua.Contains(fr, StringComparison.OrdinalIgnoreCase));

    private Task RejectAsync(HttpContext ctx, string reason)
    {
        events.Record(reason, ctx);
        return PathRules.RejectAsync(ctx, StatusCodes.Status403Forbidden);
    }
}

using AntiBotStarter.Application.Configuration;
using AntiBotStarter.Application.Security;
using AntiBotStarter.Server.Security;
using Microsoft.Extensions.Options;

namespace AntiBotStarter.Server.Middleware;

/// <summary>
/// Anti-crawler trap (link honeypot):
///  - The trap path is Disallowed in robots.txt → well-behaved bots never visit it.
///  - index.html and the Angular app contain an invisible link to it → humans never see it.
///  - Whoever visits it is a crawler that ignores robots.txt → its IP is blocked for BlockMinutes.
/// Also enforces the blocklist on every following request.
/// </summary>
public sealed class CrawlerTrapMiddleware(
    RequestDelegate next,
    IOptionsMonitor<AntiBotOptions> options,
    IBlocklist blocklist,
    SecurityEvents events)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        var o = options.CurrentValue;
        if (PathRules.IsUnder(ctx.Request.Path, o.ExemptPaths))
        {
            await next(ctx);
            return;
        }

        var key = ClientIp.Key(ctx);
        var trap = o.CrawlerTrap;

        if (trap.Enabled && ctx.Request.Path.StartsWithSegments(trap.Path, StringComparison.OrdinalIgnoreCase))
        {
            await blocklist.BlockAsync(key, TimeSpan.FromMinutes(trap.BlockMinutes), "crawler_trap");
            events.Record("crawler_trap", ctx);
            // 404 so the bot does not learn it fell into a trap.
            await PathRules.RejectAsync(ctx, StatusCodes.Status404NotFound);
            return;
        }

        if (await blocklist.IsBlockedAsync(key))
        {
            events.Record("blocked_client", ctx);
            await PathRules.RejectAsync(ctx, StatusCodes.Status403Forbidden);
            return;
        }

        await next(ctx);
    }
}

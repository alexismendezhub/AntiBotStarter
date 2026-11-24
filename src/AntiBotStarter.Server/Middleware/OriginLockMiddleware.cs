using System.Security.Cryptography;
using System.Text;
using AntiBotStarter.Application.Configuration;
using AntiBotStarter.Server.Security;
using Microsoft.Extensions.Options;

namespace AntiBotStarter.Server.Middleware;

/// <summary>
/// "Lock the origin": when the site sits behind Azure Front Door (X-Azure-FDID header)
/// or Cloudflare (secret header added with a Transform Rule), everything that arrives
/// without that header is rejected, so nobody bypasses the WAF by hitting the App Service directly.
/// Complements (does not replace) network Access Restrictions.
/// </summary>
public sealed class OriginLockMiddleware(RequestDelegate next, IOptionsMonitor<AntiBotOptions> options, SecurityEvents events)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        var s = options.CurrentValue.OriginLock;
        if (!s.Enabled || PathRules.IsUnder(ctx.Request.Path, s.ExemptPaths))
        {
            await next(ctx);
            return;
        }

        var provided = ctx.Request.Headers[s.HeaderName].ToString();
        if (provided.Length == 0 || !s.ExpectedValues.Any(v => FixedTimeEquals(v, provided)))
        {
            events.Record("origin_lock", ctx);
            await PathRules.RejectAsync(ctx, StatusCodes.Status403Forbidden);
            return;
        }

        await next(ctx);
    }

    private static bool FixedTimeEquals(string expected, string provided) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(provided));
}

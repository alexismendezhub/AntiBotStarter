using AntiBotStarter.Application.Configuration;
using Microsoft.Extensions.Options;

namespace AntiBotStarter.Server.Middleware;

/// <summary>
/// Security headers on every response, including the 403/429 produced by the other layers.
/// X-Robots-Tag tells search engines not to index (the "polite" anti-crawler layer).
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next, IOptionsMonitor<AntiBotOptions> options)
{
    private const string NoIndexAll = "noindex, nofollow, noarchive, nosnippet, noimageindex";

    public Task InvokeAsync(HttpContext ctx)
    {
        var o = options.CurrentValue;
        var h = ctx.Response.Headers;

        h["X-Content-Type-Options"] = "nosniff";
        h["X-Frame-Options"] = "DENY";
        h["Referrer-Policy"] = "strict-origin-when-cross-origin";
        h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
        h["Cross-Origin-Opener-Policy"] = "same-origin";
        h["Cross-Origin-Resource-Policy"] = "same-origin";
        h["Content-Security-Policy"] = o.ContentSecurityPolicy;

        var isApi = ctx.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase);
        if (isApi)
        {
            h["X-Robots-Tag"] = NoIndexAll;
            h.CacheControl = "no-store";
        }
        else if (!o.AllowSearchEngines)
        {
            h["X-Robots-Tag"] = NoIndexAll;
        }

        return next(ctx);
    }
}

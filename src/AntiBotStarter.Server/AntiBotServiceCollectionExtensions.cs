using AntiBotStarter.Server.Middleware;
using AntiBotStarter.Server.Security;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace AntiBotStarter.Server;

public static class AntiBotServiceCollectionExtensions
{
    /// <summary>HTTP-level protection: security events, antiforgery, rate limiting, forwarded headers, CORS and HSTS.</summary>
    public static IServiceCollection AddAntiBotProtection(this IServiceCollection services)
    {
        services.AddSingleton<SecurityEvents>();

        services.AddAntiBotAntiforgery();
        services.AddAntiBotRateLimiting();
        services.AddAntiBotForwardedHeaders();
        services.AddAntiBotCors();

        services.AddHsts(o =>
        {
            o.MaxAge = TimeSpan.FromDays(365);
            o.IncludeSubDomains = true;
        });

        return services;
    }

    /// <summary>
    /// Kestrel limits against slow connections (Slowloris) and huge bodies.
    /// They do not apply to TestServer/IIS in-process; IIS has its own limits (see deploy/iis).
    /// </summary>
    public static void HardenKestrel(KestrelServerOptions k)
    {
        k.AddServerHeader = false;
        k.Limits.MaxRequestBodySize = 1 * 1024 * 1024;
        k.Limits.MaxRequestHeadersTotalSize = 32 * 1024;
        k.Limits.MaxRequestLineSize = 8 * 1024;
        k.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(15);
        k.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(60);
        k.Limits.MinRequestBodyDataRate = new MinDataRate(bytesPerSecond: 240, gracePeriod: TimeSpan.FromSeconds(5));
        k.Limits.MaxConcurrentConnections = 2000;
        k.Limits.MaxConcurrentUpgradedConnections = 200;
    }

    /// <summary>Order matters: forwarded headers → security headers → origin lock → trap/blocklist → UA filter.</summary>
    public static IApplicationBuilder UseAntiBotEdge(this IApplicationBuilder app)
    {
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseMiddleware<OriginLockMiddleware>();
        app.UseMiddleware<CrawlerTrapMiddleware>();
        app.UseMiddleware<BotFilterMiddleware>();
        return app;
    }
}

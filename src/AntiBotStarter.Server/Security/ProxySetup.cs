using System.Net;
using AntiBotStarter.Application.Configuration;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace AntiBotStarter.Server.Security;

public static class ProxySetup
{
    public const string CorsPolicy = "spa";

    /// <summary>
    /// Real client IP behind a proxy/WAF. X-Forwarded-For is ONLY trusted when it comes from a
    /// known proxy; otherwise anyone could fake their IP and evade the rate limit.
    /// When not configured, only loopback is trusted (ASP.NET Core default behavior).
    /// </summary>
    public static IServiceCollection AddAntiBotForwardedHeaders(this IServiceCollection services)
    {
        services.AddOptions<ForwardedHeadersOptions>()
            .Configure<IOptions<AntiBotOptions>>((fh, ab) =>
            {
                var s = ab.Value.ForwardedHeaders;
                fh.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                fh.ForwardLimit = s.ForwardLimit;
                if (!string.IsNullOrWhiteSpace(s.ForwardedForHeaderName))
                    fh.ForwardedForHeaderName = s.ForwardedForHeaderName;

                if (s.KnownProxies.Length > 0 || s.KnownNetworks.Length > 0)
                {
                    fh.KnownProxies.Clear();
#pragma warning disable CS0618, ASPDEPR005 // KnownNetworks may be marked obsolete in .NET 10 (replacement: KnownIPNetworks)
                    fh.KnownNetworks.Clear();
                    foreach (var p in s.KnownProxies) fh.KnownProxies.Add(IPAddress.Parse(p));
                    foreach (var n in s.KnownNetworks)
                    {
                        var parts = n.Split('/', 2);
                        fh.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(
                            IPAddress.Parse(parts[0]), int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture)));
                    }
#pragma warning restore CS0618, ASPDEPR005
                }
            });
        return services;
    }

    /// <summary>CORS closed by default: not needed while Angular is served from the same origin.</summary>
    public static IServiceCollection AddAntiBotCors(this IServiceCollection services)
    {
        services.AddCors();
        services.AddOptions<CorsOptions>()
            .Configure<IOptions<AntiBotOptions>>((c, ab) => c.AddPolicy(CorsPolicy, p =>
            {
                var origins = ab.Value.AllowedCorsOrigins;
                if (origins.Length > 0)
                {
                    p.WithOrigins(origins)
                        .AllowCredentials()
                        .WithHeaders("Content-Type", AntiforgerySetup.HeaderName)
                        .WithMethods("GET", "POST");
                }
            }));
        return services;
    }
}

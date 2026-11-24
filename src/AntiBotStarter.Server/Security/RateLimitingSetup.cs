using System.Globalization;
using System.Threading.RateLimiting;
using AntiBotStarter.Application.Configuration;
using AntiBotStarter.Infrastructure.Configuration;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using RedisRateLimiting;
using StackExchange.Redis;

namespace AntiBotStarter.Server.Security;

public static class RateLimitPolicies
{
    /// <summary>Login, registration, confirmation: strict fixed window per IP.</summary>
    public const string Auth = "auth";

    /// <summary>Data API and token issuing: token bucket per user or IP.</summary>
    public const string Api = "api";
}

public static class RateLimitingSetup
{
    public static IServiceCollection AddAntiBotRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(_ => { });

        // Configured lazily so it reads the already-bound options (lets tests and environments override them).
        // With Distributed:RedisConnectionString the same three policies are enforced in Redis, so the limits
        // are shared by every instance instead of multiplying per instance.
        services.AddOptions<RateLimiterOptions>()
            .Configure<IOptions<AntiBotOptions>, IOptions<DistributedOptions>, IServiceProvider>((rl, ab, dist, sp) =>
            {
                var s = ab.Value.RateLimits;
                var redis = dist.Value.UseRedis;
                var prefix = dist.Value.KeyPrefix + "rl:";
                Func<IConnectionMultiplexer> mux = () => sp.GetRequiredService<IConnectionMultiplexer>();
                rl.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

                rl.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx => redis
                    ? RedisRateLimitPartition.GetSlidingWindowRateLimiter(prefix + "global:" + ClientIp.Key(ctx), _ => new RedisSlidingWindowRateLimiterOptions
                    {
                        ConnectionMultiplexerFactory = mux,
                        PermitLimit = s.GlobalPermitPerMinute,
                        Window = TimeSpan.FromMinutes(1)
                    })
                    : RateLimitPartition.GetSlidingWindowLimiter(ClientIp.Key(ctx), _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = s.GlobalPermitPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        SegmentsPerWindow = 6,
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));

                rl.AddPolicy(RateLimitPolicies.Auth, ctx => redis
                    ? RedisRateLimitPartition.GetFixedWindowRateLimiter(prefix + "auth:" + ClientIp.Key(ctx), _ => new RedisFixedWindowRateLimiterOptions
                    {
                        ConnectionMultiplexerFactory = mux,
                        PermitLimit = s.AuthPermitPerMinute,
                        Window = TimeSpan.FromMinutes(1)
                    })
                    : RateLimitPartition.GetFixedWindowLimiter(ClientIp.Key(ctx), _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = s.AuthPermitPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));

                rl.AddPolicy(RateLimitPolicies.Api, ctx =>
                {
                    var key = ctx.User.Identity?.IsAuthenticated == true
                        ? "user:" + ctx.User.Identity.Name
                        : "ip:" + ClientIp.Key(ctx);
                    if (redis)
                    {
                        return RedisRateLimitPartition.GetTokenBucketRateLimiter(prefix + "api:" + key, _ => new RedisTokenBucketRateLimiterOptions
                        {
                            ConnectionMultiplexerFactory = mux,
                            TokenLimit = s.ApiTokenLimit,
                            TokensPerPeriod = s.ApiTokensPerPeriod,
                            ReplenishmentPeriod = TimeSpan.FromSeconds(s.ApiReplenishSeconds)
                        });
                    }
                    return RateLimitPartition.GetTokenBucketLimiter(key, _ => new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = s.ApiTokenLimit,
                        TokensPerPeriod = s.ApiTokensPerPeriod,
                        ReplenishmentPeriod = TimeSpan.FromSeconds(s.ApiReplenishSeconds),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    });
                });

                rl.OnRejected = async (context, ct) =>
                {
                    var http = context.HttpContext;
                    var seconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                        ? (int)Math.Ceiling(retryAfter.TotalSeconds)
                        : 60;
                    http.Response.Headers.RetryAfter = Math.Max(1, seconds).ToString(CultureInfo.InvariantCulture);

                    http.RequestServices.GetRequiredService<SecurityEvents>().Record("rate_limited", http);

                    await http.Response.WriteAsJsonAsync(new
                    {
                        error = "rate_limited",
                        message = "Too many requests. Please try again later.",
                        retryAfterSeconds = seconds
                    }, ct);
                };
            });

        return services;
    }
}

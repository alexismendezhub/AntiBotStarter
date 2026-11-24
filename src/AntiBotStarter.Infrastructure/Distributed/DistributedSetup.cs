using AntiBotStarter.Application.Security;
using AntiBotStarter.Infrastructure.Configuration;
using AntiBotStarter.Infrastructure.Security;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace AntiBotStarter.Infrastructure.Distributed;

public static class DistributedSetup
{
    /// <summary>
    /// Memory or Redis implementations, chosen when first resolved (so tests and environments can switch by configuration).
    /// The Redis connection is only opened when Distributed:RedisConnectionString is set.
    /// </summary>
    public static IServiceCollection AddAntiBotDistributedState(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DistributedOptions>().Bind(configuration.GetSection(DistributedOptions.Section));

        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var o = sp.GetRequiredService<IOptions<DistributedOptions>>().Value;
            if (!o.UseRedis) throw new InvalidOperationException("Distributed:RedisConnectionString is not configured.");
            var cfg = ConfigurationOptions.Parse(o.RedisConnectionString);
            cfg.AbortOnConnectFail = false; // keep retrying in the background instead of failing startup
            return ConnectionMultiplexer.Connect(cfg);
        });

        services.AddSingleton<MemoryBlocklist>();
        services.AddSingleton<MemoryReplayGuard>();

        services.AddSingleton<IBlocklist>(sp => UseRedis(sp)
            ? ActivatorUtilities.CreateInstance<RedisBlocklist>(sp)
            : sp.GetRequiredService<MemoryBlocklist>());

        services.AddSingleton<IReplayGuard>(sp => UseRedis(sp)
            ? ActivatorUtilities.CreateInstance<RedisReplayGuard>(sp)
            : sp.GetRequiredService<MemoryReplayGuard>());

        services.AddHealthChecks().AddCheck<RedisHealthCheck>("redis", tags: ["ready"]);
        return services;
    }

    private static bool UseRedis(IServiceProvider sp) => sp.GetRequiredService<IOptions<DistributedOptions>>().Value.UseRedis;
}

/// <summary>Healthy when Redis is not used; otherwise PINGs it.</summary>
public sealed class RedisHealthCheck(IOptions<DistributedOptions> options, IServiceProvider services) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!options.Value.UseRedis) return HealthCheckResult.Healthy("Redis not configured (single instance).");
        try
        {
            var latency = await services.GetRequiredService<IConnectionMultiplexer>().GetDatabase().PingAsync();
            return HealthCheckResult.Healthy($"PING {latency.TotalMilliseconds:0} ms");
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            return HealthCheckResult.Unhealthy("Redis is not reachable.", ex);
        }
    }
}

using AntiBotStarter.Application.Security;
using AntiBotStarter.Infrastructure.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace AntiBotStarter.Infrastructure.Security;

public sealed class MemoryReplayGuard : IReplayGuard, IDisposable
{
    private readonly MemoryCache _used = new(new MemoryCacheOptions { SizeLimit = 200_000 });
    private readonly Lock _gate = new();

    public Task<bool> TryConsumeAsync(string nonce, TimeSpan ttl)
    {
        lock (_gate)
        {
            if (_used.TryGetValue(nonce, out _)) return Task.FromResult(false);
            _used.Set(nonce, true, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = ttl });
            return Task.FromResult(true);
        }
    }

    public void Dispose() => _used.Dispose();
}

/// <summary>SET NX with expiry: atomic across every instance.</summary>
public sealed class RedisReplayGuard(IConnectionMultiplexer redis, IOptions<DistributedOptions> options) : IReplayGuard
{
    public Task<bool> TryConsumeAsync(string nonce, TimeSpan ttl) =>
        redis.GetDatabase().StringSetAsync(options.Value.KeyPrefix + "nonce:" + nonce, 1, ttl, When.NotExists);
}

using AntiBotStarter.Application.Security;
using AntiBotStarter.Infrastructure.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace AntiBotStarter.Infrastructure.Security;

/// <summary>
/// Size-bounded in-memory blocklist (filling it cannot exhaust RAM). One instance only;
/// with several instances set Distributed:RedisConnectionString (RedisBlocklist).
/// </summary>
public sealed class MemoryBlocklist : IBlocklist, IDisposable
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 100_000 });

    public Task BlockAsync(string clientKey, TimeSpan duration, string reason)
    {
        _cache.Set("block:" + clientKey, reason, new MemoryCacheEntryOptions
        {
            Size = 1,
            AbsoluteExpirationRelativeToNow = duration
        });
        return Task.CompletedTask;
    }

    public Task<bool> IsBlockedAsync(string clientKey) => Task.FromResult(_cache.TryGetValue("block:" + clientKey, out _));

    public void Dispose() => _cache.Dispose();
}

/// <summary>Blocklist shared by every instance. Keys expire on their own (no cleanup job needed).</summary>
public sealed class RedisBlocklist(IConnectionMultiplexer redis, IOptions<DistributedOptions> options) : IBlocklist
{
    private string Key(string clientKey) => options.Value.KeyPrefix + "block:" + clientKey;

    public Task BlockAsync(string clientKey, TimeSpan duration, string reason) =>
        redis.GetDatabase().StringSetAsync(Key(clientKey), reason, duration);

    public Task<bool> IsBlockedAsync(string clientKey) =>
        redis.GetDatabase().KeyExistsAsync(Key(clientKey));
}

using System.Net;
using AntiBotStarter.Server.IntegrationTests.Support;

namespace AntiBotStarter.Server.IntegrationTests.Containers;

/// <summary>
/// Two app instances sharing one real Redis: the defenses must hold across instances
/// (a load balancer can send each request of an attacker to a different instance).
/// </summary>
public sealed class RedisTests(RedisFixture redis) : IClassFixture<RedisFixture>
{
    private (AntiBotFactory A, AntiBotFactory B) TwoInstances(params (string Key, string Value)[] extra)
    {
        var shared = new Dictionary<string, string?>
        {
            ["Distributed:RedisConnectionString"] = redis.ConnectionString,
            ["Distributed:KeyPrefix"] = "t" + Guid.NewGuid().ToString("N")[..8] + ":",
            // Same in-memory database name → both instances share the Data Protection key ring, like a real shared DB.
            ["Database:Name"] = "redis-" + Guid.NewGuid().ToString("N"),
        };
        foreach (var (k, v) in extra) shared[k] = v;
        return (new AntiBotFactory(shared), new AntiBotFactory(shared));
    }

    [SkippableFact]
    public async Task Blocklist_IsSharedAcrossInstances()
    {
        redis.EnsureAvailable();
        var (a, b) = TwoInstances();
        using (a) using (b)
        {
            Assert.Equal(HttpStatusCode.NotFound, (await a.CreateBrowserClient("203.0.113.200").GetAsync("/_internal/archive")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await b.CreateBrowserClient("203.0.113.200").GetAsync("/api/config")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await b.CreateBrowserClient("203.0.113.201").GetAsync("/api/config")).StatusCode);
        }
    }

    [SkippableFact]
    public async Task AuthRateLimit_IsSharedAcrossInstances()
    {
        redis.EnsureAvailable();
        var (a, b) = TwoInstances(("AntiBot:RateLimits:AuthPermitPerMinute", "2"));
        using (a) using (b)
        {
            var ca = a.CreateBrowserClient("203.0.113.210");
            var cb = b.CreateBrowserClient("203.0.113.210");
            Assert.Equal(HttpStatusCode.BadRequest, (await ca.PostAsJsonAsync("/api/auth/login", new { })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await cb.PostAsJsonAsync("/api/auth/login", new { })).StatusCode);
            Assert.Equal(HttpStatusCode.TooManyRequests, (await ca.PostAsJsonAsync("/api/auth/login", new { })).StatusCode);
        }
    }

    [SkippableFact]
    public async Task GlobalLimit_IsSharedAcrossInstances()
    {
        redis.EnsureAvailable();
        var (a, b) = TwoInstances(("AntiBot:RateLimits:GlobalPermitPerMinute", "3"));
        using (a) using (b)
        {
            Assert.Equal(HttpStatusCode.OK, (await a.CreateBrowserClient("203.0.113.215").GetAsync("/api/config")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await b.CreateBrowserClient("203.0.113.215").GetAsync("/api/config")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await a.CreateBrowserClient("203.0.113.215").GetAsync("/api/config")).StatusCode);
            Assert.Equal(HttpStatusCode.TooManyRequests, (await b.CreateBrowserClient("203.0.113.215").GetAsync("/api/config")).StatusCode);
        }
    }

    [SkippableFact]
    public async Task FormToken_ReplayOnAnotherInstance_IsRejected()
    {
        redis.EnsureAvailable();
        var (a, b) = TwoInstances();
        using (a) using (b)
        {
            var ca = a.CreateBrowserClient("203.0.113.220");
            await ca.PrimeAntiforgeryAsync();
            var token = await ca.GetFormTokenAsync("register");
            Assert.Equal(HttpStatusCode.Accepted, (await ca.RegisterAsync("replay@example.com", ClientExtensions.StrongPassword, formToken: token)).StatusCode);

            var cb = b.CreateBrowserClient("203.0.113.221");
            await cb.PrimeAntiforgeryAsync();
            var replay = await cb.RegisterAsync("replay2@example.com", ClientExtensions.StrongPassword, formToken: token);
            Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
            Assert.Contains("request_rejected", await replay.Content.ReadAsStringAsync());
        }
    }

    [SkippableFact]
    public async Task ReadinessCheck_IncludesRedis()
    {
        redis.EnsureAvailable();
        var (a, b) = TwoInstances();
        using (a) using (b)
        {
            var res = await a.CreateBrowserClient().GetAsync("/health/ready");
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        }
    }
}

using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Caching.Memory;

namespace AntiBotStarter.Application.Security;

public interface IDnsResolver
{
    Task<string?> ReverseLookupAsync(IPAddress ip, CancellationToken ct);
    Task<IPAddress[]> ForwardLookupAsync(string host, CancellationToken ct);
}

/// <summary>
/// Verifies that a "Googlebot"/"bingbot" is genuine (the method Google and Bing document):
/// 1) Reverse DNS of the IP → the host must end in an allowed domain (.googlebot.com, .search.msn.com…)
/// 2) Forward DNS of that host → must include the original IP.
/// Anyone can put "Googlebot" in a User-Agent; this check unmasks them.
/// </summary>
public sealed class SearchEngineVerifier(IDnsResolver dns, ILogger<SearchEngineVerifier> logger) : IDisposable
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 50_000 });

    public async Task<bool> IsVerifiedAsync(IPAddress ip, IReadOnlyCollection<string> allowedSuffixes, CancellationToken ct)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        var key = ip + "|" + string.Join(',', allowedSuffixes);
        if (_cache.TryGetValue(key, out bool cached)) return cached;

        var ok = await VerifyCoreAsync(ip, allowedSuffixes, ct);
        if (!ct.IsCancellationRequested)
        {
            _cache.Set(key, ok, new MemoryCacheEntryOptions
            {
                Size = 1,
                AbsoluteExpirationRelativeToNow = ok ? TimeSpan.FromHours(6) : TimeSpan.FromMinutes(30)
            });
        }
        return ok;
    }

    private async Task<bool> VerifyCoreAsync(IPAddress ip, IReadOnlyCollection<string> allowedSuffixes, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            var host = (await dns.ReverseLookupAsync(ip, cts.Token))?.TrimEnd('.');
            if (string.IsNullOrEmpty(host)) return false;

            // The suffix always starts with "." so "fakegooglebot.com" cannot pass as "googlebot.com".
            var suffixOk = allowedSuffixes
                .Select(s => s.StartsWith('.') ? s : "." + s)
                .Any(s => host.EndsWith(s, StringComparison.OrdinalIgnoreCase));
            if (!suffixOk) return false;

            var addresses = await dns.ForwardLookupAsync(host, cts.Token);
            return addresses.Any(a => (a.IsIPv4MappedToIPv6 ? a.MapToIPv4() : a).Equals(ip));
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or ArgumentException)
        {
            logger.LogInformation(ex, "Could not verify search engine for {Ip}", ip);
            return false;
        }
    }

    public void Dispose() => _cache.Dispose();
}

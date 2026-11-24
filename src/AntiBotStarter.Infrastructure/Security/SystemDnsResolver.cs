using System.Net;
using System.Net.Sockets;
using AntiBotStarter.Application.Security;
using Microsoft.Extensions.Caching.Memory;

namespace AntiBotStarter.Infrastructure.Security;

public sealed class SystemDnsResolver : IDnsResolver
{
    public async Task<string?> ReverseLookupAsync(IPAddress ip, CancellationToken ct)
    {
        var entry = await Dns.GetHostEntryAsync(ip.ToString(), ct);
        return entry.HostName;
    }

    public Task<IPAddress[]> ForwardLookupAsync(string host, CancellationToken ct) =>
        Dns.GetHostAddressesAsync(host, ct);
}

using System.Net;
using System.Net.Sockets;

namespace AntiBotStarter.Server.Security;

/// <summary>
/// Client key used for rate limiting and the blocklist.
/// IPv6 is grouped by /64 prefix because an attacker with IPv6 usually controls a whole /64
/// and could rotate addresses to evade the limits.
/// </summary>
public static class ClientIp
{
    public static string Key(HttpContext ctx) => Key(ctx.Connection.RemoteIpAddress);

    public static string Key(IPAddress? ip)
    {
        if (ip is null) return "unknown";
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (ip.AddressFamily == AddressFamily.InterNetworkV6 && !IPAddress.IsLoopback(ip))
        {
            var bytes = ip.GetAddressBytes();
            for (var i = 8; i < 16; i++) bytes[i] = 0;
            return new IPAddress(bytes) + "/64";
        }
        return ip.ToString();
    }

    public static string? Address(HttpContext ctx)
    {
        var ip = ctx.Connection.RemoteIpAddress;
        if (ip is null) return null;
        return (ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip).ToString();
    }
}

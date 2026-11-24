using System.Security.Cryptography;

namespace AntiBotStarter.Server.IntegrationTests.Support;

/// <summary>RFC 6238 TOTP (HMAC-SHA1, 30 s, 6 digits) — what an authenticator app computes from the shared key.</summary>
public static class Totp
{
    public static string Code(string sharedKey, DateTimeOffset? at = null, int stepOffset = 0)
    {
        var key = Base32Decode(sharedKey.Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant());
        var counter = (long)Math.Floor((at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds() / 30.0) + stepOffset;
        var msg = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian) Array.Reverse(msg);

#pragma warning disable CA5350 // RFC 6238 default algorithm, as used by authenticator apps
        var hash = HMACSHA1.HashData(key, msg);
#pragma warning restore CA5350
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static byte[] Base32Decode(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var output = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in input.TrimEnd('='))
        {
            var value = alphabet.IndexOf(c, StringComparison.Ordinal);
            if (value < 0) throw new FormatException($"Invalid base32 character '{c}'.");
            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }
        return [.. output];
    }
}

using System.Security.Cryptography;
using System.Text;
using AntiBotStarter.Application.Configuration;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace AntiBotStarter.Infrastructure.Identity;

/// <summary>
/// Rejects passwords found in breaches (Have I Been Pwned, k-anonymity:
/// only the first 5 characters of the SHA-1 are sent, never the password).
/// Defense against credential stuffing, the most common bot attack against login.
/// </summary>
public sealed class PwnedPasswordValidator(
    IHttpClientFactory httpFactory,
    IOptionsMonitor<AntiBotOptions> options,
    ILogger<PwnedPasswordValidator> logger) : IPasswordValidator<IdentityUser>
{
    public const string HttpClientName = "hibp";

    public async Task<IdentityResult> ValidateAsync(UserManager<IdentityUser> manager, IdentityUser user, string? password)
    {
        var s = options.CurrentValue.PwnedPasswords;
        if (!s.Enabled || string.IsNullOrEmpty(password)) return IdentityResult.Success;

#pragma warning disable CA5350 // SHA-1 is required by the HIBP API; it does not protect anything
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password)));
#pragma warning restore CA5350
        var prefix = hash[..5];
        var suffix = hash[5..];

        try
        {
            var client = httpFactory.CreateClient(HttpClientName);
            var body = await client.GetStringAsync($"range/{prefix}");
            foreach (var line in body.Split('\n'))
            {
                var parts = line.Trim().Split(':');
                if (parts.Length == 2 &&
                    parts[0].Equals(suffix, StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(parts[1], out var count) && count > 0)
                {
                    return IdentityResult.Failed(new IdentityError
                    {
                        Code = "PwnedPassword",
                        Description = "This password appears in known data breaches. Choose another one."
                    });
                }
            }
            return IdentityResult.Success;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Could not query Have I Been Pwned");
            return s.FailClosed
                ? IdentityResult.Failed(new IdentityError
                {
                    Code = "PwnedPasswordUnavailable",
                    Description = "We could not validate the password right now. Please try again later."
                })
                : IdentityResult.Success;
        }
    }
}

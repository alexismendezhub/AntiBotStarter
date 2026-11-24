using System.Security.Cryptography;
using System.Text;
using AntiBotStarter.Application.Configuration;
using AntiBotStarter.Infrastructure.Identity;
using AntiBotStarter.Infrastructure.UnitTests.Support;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;

namespace AntiBotStarter.Infrastructure.UnitTests;

public sealed class PwnedPasswordValidatorTests
{
    private const string Password = "Test-Password-For-Hibp-1!";

    private static (string Prefix, string Suffix) Split(string password)
    {
#pragma warning disable CA5350 // SHA-1 is what the HIBP range API uses
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password)));
#pragma warning restore CA5350
        return (hash[..5], hash[5..]);
    }

    private static PwnedPasswordValidator Create(StubHandler handler, bool failClosed = false) =>
        new(new StubHttpClientFactory(handler, new Uri("https://api.pwnedpasswords.com/")),
            new StaticOptionsMonitor<AntiBotOptions>(new AntiBotOptions
            {
                PwnedPasswords = new PwnedPasswordSettings { Enabled = true, FailClosed = failClosed }
            }),
            NullLogger<PwnedPasswordValidator>.Instance);

    [Fact]
    public async Task BreachedPassword_IsRejected_AndOnlyPrefixIsSent()
    {
        var (prefix, suffix) = Split(Password);
        var handler = StubHandler.Text($"0000000000000000000000000000000000A:3\r\n{suffix}:42\r\n");
        var result = await Create(handler).ValidateAsync(null!, new IdentityUser(), Password);

        Assert.False(result.Succeeded);
        Assert.Equal("PwnedPassword", result.Errors.Single().Code);
        var url = handler.Requests.Single().Request.RequestUri!.ToString();
        Assert.EndsWith("/range/" + prefix, url);
        Assert.DoesNotContain(suffix, url);
    }

    [Fact]
    public async Task PaddingEntriesWithZeroCount_AreIgnored()
    {
        var (_, suffix) = Split(Password);
        var result = await Create(StubHandler.Text($"{suffix}:0\r\n")).ValidateAsync(null!, new IdentityUser(), Password);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task CleanPassword_IsAccepted()
    {
        var result = await Create(StubHandler.Text("0000000000000000000000000000000000A:3\r\n")).ValidateAsync(null!, new IdentityUser(), Password);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task NetworkError_FailOpen_ByDefault()
    {
        Assert.True((await Create(StubHandler.Throws()).ValidateAsync(null!, new IdentityUser(), Password)).Succeeded);
    }

    [Fact]
    public async Task NetworkError_FailClosed_WhenConfigured()
    {
        var result = await Create(StubHandler.Throws(), failClosed: true).ValidateAsync(null!, new IdentityUser(), Password);
        Assert.False(result.Succeeded);
        Assert.Equal("PwnedPasswordUnavailable", result.Errors.Single().Code);
    }
}

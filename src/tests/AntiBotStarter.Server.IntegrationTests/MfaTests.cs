using System.Net;
using System.Text.Json;
using AntiBotStarter.Server.Endpoints;
using AntiBotStarter.Server.IntegrationTests.Support;

namespace AntiBotStarter.Server.IntegrationTests;

public sealed class MfaTests
{
    private sealed record SetupDto(string SharedKey, string AuthenticatorUri);
    private sealed record CodesDto(string[] RecoveryCodes);
    private sealed record StatusDto(bool Enabled, int RecoveryCodesLeft);

    /// <summary>Registers, confirms and signs in; returns the client with a fresh antiforgery token.</summary>
    private static async Task<HttpClient> SignedInAsync(AntiBotFactory f, string email)
    {
        var c = f.CreateBrowserClient();
        await c.RegisterAndConfirmAsync(f, email);
        Assert.Equal(HttpStatusCode.OK, (await c.LoginAsync(email, ClientExtensions.StrongPassword)).StatusCode);
        await c.PrimeAntiforgeryAsync();
        return c;
    }

    private static async Task<(string Key, string[] Codes)> EnableAsync(HttpClient c)
    {
        var setup = await (await c.PostAsync("/api/account/mfa/setup", null)).Content.ReadFromJsonAsync<SetupDto>();
        var enable = await c.PostAsJsonAsync("/api/account/mfa/enable", new { code = Totp.Code(setup!.SharedKey) });
        Assert.Equal(HttpStatusCode.OK, enable.StatusCode);
        var codes = await enable.Content.ReadFromJsonAsync<CodesDto>();
        await c.PrimeAntiforgeryAsync();
        return (setup.SharedKey, codes!.RecoveryCodes);
    }

    private static async Task<HttpClient> PasswordStepAsync(AntiBotFactory f, string email)
    {
        var c = f.CreateBrowserClient();
        await c.PrimeAntiforgeryAsync();
        var login = await c.LoginAsync(email, ClientExtensions.StrongPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.True(JsonDocument.Parse(await login.Content.ReadAsStringAsync()).RootElement.GetProperty("requiresTwoFactor").GetBoolean());
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/auth/me")).StatusCode); // not signed in yet
        return c;
    }

    [Fact]
    public async Task Setup_ReturnsKeyAndOtpauthUri()
    {
        using var f = new AntiBotFactory();
        var c = await SignedInAsync(f, "setup@example.com");
        var setup = await (await c.PostAsync("/api/account/mfa/setup", null)).Content.ReadFromJsonAsync<SetupDto>();
        Assert.StartsWith("otpauth://totp/AntiBotStarter:setup%40example.com?secret=", setup!.AuthenticatorUri);
        Assert.Contains("issuer=AntiBotStarter", setup.AuthenticatorUri);
        Assert.False(string.IsNullOrWhiteSpace(setup.SharedKey));
    }

    [Fact]
    public async Task Enable_RequiresAValidCode()
    {
        using var f = new AntiBotFactory();
        var c = await SignedInAsync(f, "badcode@example.com");
        await c.PostAsync("/api/account/mfa/setup", null);
        var res = await c.PostAsJsonAsync("/api/account/mfa/enable", new { code = "000000" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var status = await c.GetFromJsonAsync<StatusDto>("/api/account/mfa");
        Assert.False(status!.Enabled);
    }

    [Fact]
    public async Task FullFlow_Enable_LoginNeedsCode_ThenSucceeds()
    {
        using var f = new AntiBotFactory();
        var c = await SignedInAsync(f, "mfa@example.com");
        var (key, codes) = await EnableAsync(c);
        Assert.Equal(10, codes.Length);
        Assert.True((await c.GetFromJsonAsync<StatusDto>("/api/account/mfa"))!.Enabled);

        var second = await PasswordStepAsync(f, "mfa@example.com");
        var ok = await second.PostAsJsonAsync("/api/auth/login-2fa", new { code = Totp.Code(key), rememberMachine = false });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Contains("mfa@example.com", await second.GetStringAsync("/api/auth/me"));
    }

    [Fact]
    public async Task WrongCodes_LockTheAccount()
    {
        using var f = new AntiBotFactory();
        var c = await SignedInAsync(f, "lock2fa@example.com");
        var (key, _) = await EnableAsync(c);

        var second = await PasswordStepAsync(f, "lock2fa@example.com");
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await second.PostAsJsonAsync("/api/auth/login-2fa", new { code = "123456", rememberMachine = false })).StatusCode);

        var correct = await second.PostAsJsonAsync("/api/auth/login-2fa", new { code = Totp.Code(key), rememberMachine = false });
        Assert.Equal(HttpStatusCode.Unauthorized, correct.StatusCode);
    }

    [Fact]
    public async Task RecoveryCode_SignsIn_AndIsSingleUse()
    {
        using var f = new AntiBotFactory();
        var c = await SignedInAsync(f, "recovery@example.com");
        var (_, codes) = await EnableAsync(c);

        var first = await PasswordStepAsync(f, "recovery@example.com");
        Assert.Equal(HttpStatusCode.OK, (await first.PostAsJsonAsync("/api/auth/login-recovery", new { recoveryCode = codes[0] })).StatusCode);
        Assert.Equal(9, (await first.GetFromJsonAsync<StatusDto>("/api/account/mfa"))!.RecoveryCodesLeft);

        var reuse = await PasswordStepAsync(f, "recovery@example.com");
        Assert.Equal(HttpStatusCode.Unauthorized, (await reuse.PostAsJsonAsync("/api/auth/login-recovery", new { recoveryCode = codes[0] })).StatusCode);
    }

    [Fact]
    public async Task SecondStep_WithoutPasswordStep_IsRejected()
    {
        using var f = new AntiBotFactory();
        var c = f.CreateBrowserClient();
        await c.PrimeAntiforgeryAsync();
        var res = await c.PostAsJsonAsync("/api/auth/login-2fa", new { code = "123456", rememberMachine = false });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Contains("two_factor_expired", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Disable_RequiresCurrentCode()
    {
        using var f = new AntiBotFactory();
        var c = await SignedInAsync(f, "disable@example.com");
        var (key, _) = await EnableAsync(c);

        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/account/mfa/disable", new { code = "000000" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/api/account/mfa/disable", new { code = Totp.Code(key) })).StatusCode);
        Assert.False((await c.GetFromJsonAsync<StatusDto>("/api/account/mfa"))!.Enabled);
    }

    [Fact]
    public async Task Setup_WhenAlreadyEnabled_IsConflict()
    {
        using var f = new AntiBotFactory();
        var c = await SignedInAsync(f, "conflict@example.com");
        await EnableAsync(c);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsync("/api/account/mfa/setup", null)).StatusCode);
    }

    [Fact]
    public async Task MfaEndpoints_RequireSessionAndAntiforgery()
    {
        using var f = new AntiBotFactory();
        var anonymous = f.CreateBrowserClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/account/mfa")).StatusCode);

        var c = await SignedInAsync(f, "csrf@example.com");
        c.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync("/api/account/mfa/setup", null)).StatusCode);
    }

    [Fact]
    public async Task TwoFactorCookie_IsHostPrefixed_HttpOnly_Secure_Strict()
    {
        using var f = new AntiBotFactory();
        var c = await SignedInAsync(f, "cookie2fa@example.com");
        await EnableAsync(c);

        var fresh = f.CreateBrowserClient();
        await fresh.PrimeAntiforgeryAsync();
        var login = await fresh.LoginAsync("cookie2fa@example.com", ClientExtensions.StrongPassword);
        var cookie = login.Headers.GetValues("Set-Cookie").Single(h => h.StartsWith("__Host-abs-2fa=", StringComparison.Ordinal)).ToLowerInvariant();
        Assert.Contains("httponly", cookie);
        Assert.Contains("secure", cookie);
        Assert.Contains("samesite=strict", cookie);
    }

    [Theory]
    [InlineData("123 456", "123456")]
    [InlineData("123-456", "123456")]
    [InlineData("12345", null)]
    [InlineData("abcdef", null)]
    [InlineData(null, null)]
    public void CleanCode_AcceptsOnlySixDigits(string? raw, string? expected) =>
        Assert.Equal(expected, AntiBotStarter.Server.Endpoints.MfaEndpoints.CleanCode(raw));
}

using System.Net;
using AntiBotStarter.Server.IntegrationTests.Support;

namespace AntiBotStarter.Server.IntegrationTests;

public sealed class AccountRecoveryTests
{
    private const string NewPassword = "Brand-New-Password-2026!x";

    [Fact]
    public async Task ForgotPassword_SameAnswerForUnknownUnconfirmedAndReal_EmailOnlyForReal()
    {
        using var f = new AntiBotFactory();
        var c = f.CreateBrowserClient();
        await c.RegisterAndConfirmAsync(f, "real@example.com");
        await c.RegisterAsync("unconfirmed@example.com", ClientExtensions.StrongPassword);

        var real = await c.ForgotPasswordAsync("real@example.com");
        var unknown = await c.ForgotPasswordAsync("nobody@example.com");
        var unconfirmed = await c.ForgotPasswordAsync("unconfirmed@example.com");

        Assert.All(new[] { real, unknown, unconfirmed }, r => Assert.Equal(HttpStatusCode.Accepted, r.StatusCode));
        var body = await real.Content.ReadAsStringAsync();
        Assert.Equal(body, await unknown.Content.ReadAsStringAsync());
        Assert.Equal(body, await unconfirmed.Content.ReadAsStringAsync());

        Assert.Equal(1, f.Emails.ResetCountFor("real@example.com"));
        Assert.Equal(0, f.Emails.ResetCountFor("nobody@example.com"));
        Assert.Equal(0, f.Emails.ResetCountFor("unconfirmed@example.com"));
        Assert.StartsWith("https://localhost/reset-password?email=", f.Emails.LastResetLinkFor("real@example.com"));
    }

    [Fact]
    public async Task ResetPassword_WithLink_ChangesPassword_AndClearsLockout()
    {
        using var f = new AntiBotFactory();
        var c = f.CreateBrowserClient();
        await c.RegisterAndConfirmAsync(f, "reset@example.com");

        // An attacker locks the account by guessing passwords.
        for (var i = 0; i < 5; i++) await c.LoginAsync("reset@example.com", $"Wrong-{i}-Long-Password!x");
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.LoginAsync("reset@example.com", ClientExtensions.StrongPassword)).StatusCode);

        await c.ForgotPasswordAsync("reset@example.com");
        var link = f.Emails.LastResetLinkFor("reset@example.com");
        var res = await c.ResetPasswordAsync(ClientExtensions.QueryValue(link, "email"), ClientExtensions.QueryValue(link, "code"), NewPassword);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await c.LoginAsync("reset@example.com", ClientExtensions.StrongPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.LoginAsync("reset@example.com", NewPassword)).StatusCode);
    }

    [Fact]
    public async Task ResetLink_IsSingleUse()
    {
        using var f = new AntiBotFactory();
        var c = f.CreateBrowserClient();
        await c.RegisterAndConfirmAsync(f, "once@example.com");
        await c.ForgotPasswordAsync("once@example.com");
        var link = f.Emails.LastResetLinkFor("once@example.com");
        var code = ClientExtensions.QueryValue(link, "code");

        Assert.Equal(HttpStatusCode.OK, (await c.ResetPasswordAsync("once@example.com", code, NewPassword)).StatusCode);
        var again = await c.ResetPasswordAsync("once@example.com", code, "Another-Password-2026!x");
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Contains("invalid_reset", await again.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("reset2@example.com", "bm90LWEtdG9rZW4")]
    [InlineData("nobody@example.com", "bm90LWEtdG9rZW4")]
    [InlineData("reset2@example.com", "%%%not-base64%%%")]
    public async Task InvalidReset_IsGeneric(string email, string code)
    {
        using var f = new AntiBotFactory();
        var c = f.CreateBrowserClient();
        await c.RegisterAndConfirmAsync(f, "reset2@example.com");
        var res = await c.ResetPasswordAsync(email, code, NewPassword);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("invalid_reset", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ResetPassword_StillEnforcesPasswordRules()
    {
        using var f = new AntiBotFactory();
        var c = f.CreateBrowserClient();
        await c.RegisterAndConfirmAsync(f, "weakreset@example.com");
        await c.ForgotPasswordAsync("weakreset@example.com");
        var link = f.Emails.LastResetLinkFor("weakreset@example.com");
        var res = await c.ResetPasswordAsync("weakreset@example.com", ClientExtensions.QueryValue(link, "code"), "short");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("invalid_password", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ResendConfirmation_OnlyForUnconfirmedAccounts_SameAnswerAlways()
    {
        using var f = new AntiBotFactory();
        var c = f.CreateBrowserClient();
        await c.RegisterAndConfirmAsync(f, "confirmed@example.com");
        await c.RegisterAsync("pending@example.com", ClientExtensions.StrongPassword);

        var a = await c.ResendConfirmationAsync("confirmed@example.com");
        var b = await c.ResendConfirmationAsync("pending@example.com");
        var d = await c.ResendConfirmationAsync("nobody@example.com");

        Assert.Equal(await a.Content.ReadAsStringAsync(), await b.Content.ReadAsStringAsync());
        Assert.Equal(await a.Content.ReadAsStringAsync(), await d.Content.ReadAsStringAsync());
        Assert.Equal(1, f.Emails.CountFor("confirmed@example.com"));
        Assert.Equal(2, f.Emails.CountFor("pending@example.com"));
    }

    [Fact]
    public async Task NewForms_RequireAntiforgeryTokenAndCaptcha()
    {
        using var f = new AntiBotFactory();
        var c = f.CreateBrowserClient();

        Assert.Equal(HttpStatusCode.BadRequest, (await c.ForgotPasswordAsync("x@example.com")).StatusCode); // no antiforgery

        await c.PrimeAntiforgeryAsync();
        var noCaptcha = await c.PostAsJsonAsync("/api/auth/forgot-password",
            new { email = "x@example.com", website = "", formToken = await c.GetFormTokenAsync("forgot"), captchaToken = "bad" });
        Assert.Contains("captcha_failed", await noCaptcha.Content.ReadAsStringAsync());

        var wrongPurpose = await c.PostAsJsonAsync("/api/auth/resend-confirmation",
            new { email = "x@example.com", website = "", formToken = await c.GetFormTokenAsync("forgot"), captchaToken = FakeCaptchaVerifier.Pass });
        Assert.Contains("request_rejected", await wrongPurpose.Content.ReadAsStringAsync());

        var honeypot = await c.PostAsJsonAsync("/api/auth/forgot-password",
            new { email = "x@example.com", website = "spam", formToken = await c.GetFormTokenAsync("forgot"), captchaToken = FakeCaptchaVerifier.Pass });
        Assert.Contains("request_rejected", await honeypot.Content.ReadAsStringAsync());
    }
}

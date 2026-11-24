using System.Net;
using AntiBotStarter.Server.IntegrationTests.Support;

namespace AntiBotStarter.Server.IntegrationTests;

public sealed class FormProtectionTests
{
    [Fact]
    public async Task WithoutAntiforgery_Returns400()
    {
        using var f = new AntiBotFactory();
        var c = f.CreateBrowserClient();
        var res = await c.RegisterAsync("a@example.com", ClientExtensions.StrongPassword);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("antiforgery_failed", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task HoneypotFilled_Returns400_AndNoUserCreated()
    {
        using var f = new AntiBotFactory();
        var c = f.CreateBrowserClient();
        await c.PrimeAntiforgeryAsync();
        var res = await c.RegisterAsync("bot@example.com", ClientExtensions.StrongPassword, website: "http://spam.example");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("request_rejected", await res.Content.ReadAsStringAsync());
        Assert.Equal(0, f.Emails.CountFor("bot@example.com"));
    }

    [Fact]
    public async Task MissingOrForgedFormToken_Returns400()
    {
        using var f = new AntiBotFactory();
        var c = f.CreateBrowserClient();
        await c.PrimeAntiforgeryAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.RegisterAsync("x@example.com", ClientExtensions.StrongPassword, formToken: "")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.RegisterAsync("x@example.com", ClientExtensions.StrongPassword, formToken: "CfDJ8-fake")).StatusCode);
    }

    [Fact]
    public async Task ReusedFormToken_Returns400()
    {
        using var f = new AntiBotFactory();
        var c = f.CreateBrowserClient();
        await c.PrimeAntiforgeryAsync();
        var token = await c.GetFormTokenAsync("register");

        Assert.Equal(HttpStatusCode.Accepted, (await c.RegisterAsync("one@example.com", ClientExtensions.StrongPassword, formToken: token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.RegisterAsync("two@example.com", ClientExtensions.StrongPassword, formToken: token)).StatusCode);
    }

    [Fact]
    public async Task TokenForOtherPurpose_Returns400()
    {
        using var f = new AntiBotFactory();
        var c = f.CreateBrowserClient();
        await c.PrimeAntiforgeryAsync();
        var loginToken = await c.GetFormTokenAsync("login");
        Assert.Equal(HttpStatusCode.BadRequest, (await c.RegisterAsync("p@example.com", ClientExtensions.StrongPassword, formToken: loginToken)).StatusCode);
    }

    [Fact]
    public async Task SubmittingTooFast_Returns400()
    {
        using var f = new AntiBotFactory(new Dictionary<string, string?> { ["AntiBot:Forms:MinSecondsToSubmit"] = "30" });
        var c = f.CreateBrowserClient();
        await c.PrimeAntiforgeryAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.RegisterAsync("fast@example.com", ClientExtensions.StrongPassword)).StatusCode);
    }

    [Fact]
    public async Task CaptchaFailed_Returns400_WithCaptchaError()
    {
        using var f = new AntiBotFactory();
        var c = f.CreateBrowserClient();
        await c.PrimeAntiforgeryAsync();
        var res = await c.RegisterAsync("c@example.com", ClientExtensions.StrongPassword, captcha: "invalid-token");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("captcha_failed", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CaptchaDisabled_SkipsVerification()
    {
        using var f = new AntiBotFactory(new Dictionary<string, string?> { ["AntiBot:Captcha:Enabled"] = "false" });
        var c = f.CreateBrowserClient();
        await c.PrimeAntiforgeryAsync();
        Assert.Equal(HttpStatusCode.Accepted, (await c.RegisterAsync("nocap@example.com", ClientExtensions.StrongPassword, captcha: null)).StatusCode);
        Assert.Equal(0, f.Captcha.Calls);
    }

    [Fact]
    public async Task FormTokenEndpoint_RejectsUnknownPurpose()
    {
        using var f = new AntiBotFactory();
        Assert.Equal(HttpStatusCode.BadRequest, (await f.CreateBrowserClient().GetAsync("/api/forms/token?purpose=other")).StatusCode);
    }
}

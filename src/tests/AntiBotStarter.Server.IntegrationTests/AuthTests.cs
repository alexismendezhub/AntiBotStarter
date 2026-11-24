using System.Net;
using AntiBotStarter.Server.IntegrationTests.Support;

namespace AntiBotStarter.Server.IntegrationTests;

public sealed class AuthTests
{
    [Fact]
    public async Task Register_Confirm_Login_Me_Logout()
    {
        using var f = new AntiBotFactory();
        var c = f.CreateBrowserClient();
        await c.RegisterAndConfirmAsync(f, "alice@example.com");

        var link = f.Emails.LastLinkFor("alice@example.com");
        Assert.StartsWith("https://localhost/confirm-email?", link);

        Assert.Equal(HttpStatusCode.OK, (await c.LoginAsync("alice@example.com", ClientExtensions.StrongPassword)).StatusCode);
        Assert.Contains("alice@example.com", await c.GetStringAsync("/api/auth/me"));

        await c.PrimeAntiforgeryAsync(); // the antiforgery token changes with the identity
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task ConfirmationLink_UsesConfiguredBaseUrl_NotHostHeader()
    {
        using var f = new AntiBotFactory(new Dictionary<string, string?> { ["App:PublicBaseUrl"] = "https://www.my-site.example" });
        var c = f.CreateBrowserClient();
        await c.PrimeAntiforgeryAsync();
        await c.RegisterAsync("host@example.com", ClientExtensions.StrongPassword);
        Assert.StartsWith("https://www.my-site.example/confirm-email?", f.Emails.LastLinkFor("host@example.com"));
    }

    [Fact]
    public async Task Login_Unconfirmed_Fails()
    {
        using var f = new AntiBotFactory();
        var c = f.CreateBrowserClient();
        await c.PrimeAntiforgeryAsync();
        await c.RegisterAsync("unconfirmed@example.com", ClientExtensions.StrongPassword);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.LoginAsync("unconfirmed@example.com", ClientExtensions.StrongPassword)).StatusCode);
    }

    [Fact]
    public async Task UnknownUser_And_WrongPassword_GiveIdenticalResponses()
    {
        using var f = new AntiBotFactory();
        var c = f.CreateBrowserClient();
        await c.RegisterAndConfirmAsync(f, "real@example.com");

        var unknown = await c.LoginAsync("missing@example.com", ClientExtensions.StrongPassword);
        var wrong = await c.LoginAsync("real@example.com", "Wrong-Long-Password-2026!x");

        Assert.Equal(unknown.StatusCode, wrong.StatusCode);
        Assert.Equal(await unknown.Content.ReadAsStringAsync(), await wrong.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Lockout_AfterFiveFailures_EvenWithCorrectPassword()
    {
        using var f = new AntiBotFactory();
        var c = f.CreateBrowserClient();
        await c.RegisterAndConfirmAsync(f, "victim@example.com");

        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await c.LoginAsync("victim@example.com", $"Wrong-{i}-Long-Password!x")).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await c.LoginAsync("victim@example.com", ClientExtensions.StrongPassword)).StatusCode);
    }

    [Fact]
    public async Task RegisterExistingEmail_Returns202_WithoutRevealingIt()
    {
        using var f = new AntiBotFactory();
        var c = f.CreateBrowserClient();
        await c.RegisterAndConfirmAsync(f, "dup@example.com");

        var second = await c.RegisterAsync("dup@example.com", ClientExtensions.StrongPassword);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        Assert.Equal(1, f.Emails.CountFor("dup@example.com"));
    }

    [Theory]
    [InlineData("short1!A")]
    [InlineData("no-uppercase-or-digits")]
    public async Task WeakPassword_Returns400(string password)
    {
        using var f = new AntiBotFactory();
        var c = f.CreateBrowserClient();
        await c.PrimeAntiforgeryAsync();
        var res = await c.RegisterAsync("weak@example.com", password);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("invalid_password", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task InvalidEmail_Returns400()
    {
        using var f = new AntiBotFactory();
        var c = f.CreateBrowserClient();
        await c.PrimeAntiforgeryAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.RegisterAsync("not-an-email", ClientExtensions.StrongPassword)).StatusCode);
    }

    [Fact]
    public async Task ConfirmWithBadCode_Returns400()
    {
        using var f = new AntiBotFactory();
        var c = f.CreateBrowserClient();
        await c.PrimeAntiforgeryAsync();
        var res = await c.PostAsJsonAsync("/api/auth/confirm-email", new { userId = Guid.NewGuid().ToString(), code = "abc" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Me_Unauthenticated_Returns401_NotRedirect()
    {
        using var f = new AntiBotFactory();
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.CreateBrowserClient().GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task AuthCookie_IsHostPrefixed_HttpOnly_Secure_Strict()
    {
        using var f = new AntiBotFactory();
        var c = f.CreateBrowserClient();
        await c.RegisterAndConfirmAsync(f, "cookie@example.com");
        var res = await c.LoginAsync("cookie@example.com", ClientExtensions.StrongPassword);

        var cookie = res.Headers.GetValues("Set-Cookie").Single(h => h.StartsWith("__Host-abs-auth=", StringComparison.Ordinal)).ToLowerInvariant();
        Assert.Contains("httponly", cookie);
        Assert.Contains("secure", cookie);
        Assert.Contains("samesite=strict", cookie);
    }
}

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace AntiBotStarter.Server.IntegrationTests.Support;

public static class ClientExtensions
{
    public const string StrongPassword = "Correct-Long-Password-2026!x";

    /// <summary>Does what Angular does on startup: gets the antiforgery cookies and sets the header.</summary>
    public static async Task PrimeAntiforgeryAsync(this HttpClient client)
    {
        var response = await client.GetAsync("/api/antiforgery");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var cookie = response.Headers.GetValues("Set-Cookie").First(h => h.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal));
        var value = Uri.UnescapeDataString(cookie.Split(';')[0]["XSRF-TOKEN=".Length..]);

        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", value);
    }

    public static async Task<string> GetFormTokenAsync(this HttpClient client, string purpose)
    {
        var dto = await client.GetFromJsonAsync<TokenDto>($"/api/forms/token?purpose={purpose}");
        return dto!.Token;
    }

    public static async Task<HttpResponseMessage> RegisterAsync(this HttpClient client, string email, string password,
        string? website = "", string? captcha = FakeCaptchaVerifier.Pass, string? formToken = null)
    {
        formToken ??= await client.GetFormTokenAsync("register");
        return await client.PostAsJsonAsync("/api/auth/register",
            new { email, password, website, formToken, captchaToken = captcha });
    }

    public static async Task<HttpResponseMessage> LoginAsync(this HttpClient client, string email, string password)
    {
        var formToken = await client.GetFormTokenAsync("login");
        return await client.PostAsJsonAsync("/api/auth/login",
            new { email, password, rememberMe = false, website = "", formToken, captchaToken = FakeCaptchaVerifier.Pass });
    }

    public static async Task RegisterAndConfirmAsync(this HttpClient client, AntiBotFactory factory, string email, string password = StrongPassword)
    {
        await client.PrimeAntiforgeryAsync();
        var register = await client.RegisterAsync(email, password);
        Assert.Equal(HttpStatusCode.Accepted, register.StatusCode);

        var link = new Uri(factory.Emails.LastLinkFor(email));
        var query = QueryHelpers.ParseQuery(link.Query);
        var confirm = await client.PostAsJsonAsync("/api/auth/confirm-email",
            new { userId = query["userId"].ToString(), code = query["code"].ToString() });
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
    }

    public static async Task<HttpResponseMessage> PostEmailFormAsync(this HttpClient client, string path, string purpose, string email)
    {
        var formToken = await client.GetFormTokenAsync(purpose);
        return await client.PostAsJsonAsync(path, new { email, website = "", formToken, captchaToken = FakeCaptchaVerifier.Pass });
    }

    public static Task<HttpResponseMessage> ResendConfirmationAsync(this HttpClient client, string email) =>
        client.PostEmailFormAsync("/api/auth/resend-confirmation", "resend", email);

    public static Task<HttpResponseMessage> ForgotPasswordAsync(this HttpClient client, string email) =>
        client.PostEmailFormAsync("/api/auth/forgot-password", "forgot", email);

    public static async Task<HttpResponseMessage> ResetPasswordAsync(this HttpClient client, string email, string code, string newPassword)
    {
        var formToken = await client.GetFormTokenAsync("reset");
        return await client.PostAsJsonAsync("/api/auth/reset-password",
            new { email, code, newPassword, website = "", formToken, captchaToken = FakeCaptchaVerifier.Pass });
    }

    public static string QueryValue(string link, string name) =>
        QueryHelpers.ParseQuery(new Uri(link).Query)[name].ToString();

    public sealed record TokenDto(string Token);
}

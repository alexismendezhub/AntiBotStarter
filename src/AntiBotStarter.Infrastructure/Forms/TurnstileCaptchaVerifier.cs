using System.Text.Json;
using System.Text.Json.Serialization;
using AntiBotStarter.Application.Configuration;
using AntiBotStarter.Application.Forms;
using Microsoft.Extensions.Options;

namespace AntiBotStarter.Infrastructure.Forms;

/// <summary>
/// Server-side Cloudflare Turnstile verification (mandatory: validating only in the
/// browser is useless). Fails closed: if Cloudflare does not answer, the request is rejected.
/// For reCAPTCHA v3/Enterprise or hCaptcha, just add another ICaptchaVerifier implementation.
/// </summary>
public sealed class TurnstileCaptchaVerifier(
    IHttpClientFactory httpFactory,
    IOptionsMonitor<AntiBotOptions> options,
    ILogger<TurnstileCaptchaVerifier> logger) : ICaptchaVerifier
{
    public const string HttpClientName = "turnstile";

    public async Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken ct)
    {
        // Turnstile issues tokens of up to 2048 characters.
        if (string.IsNullOrWhiteSpace(token) || token.Length > 2048) return false;

        var s = options.CurrentValue.Captcha;
        var form = new Dictionary<string, string> { ["secret"] = s.SecretKey, ["response"] = token };
        if (!string.IsNullOrEmpty(remoteIp)) form["remoteip"] = remoteIp;

        try
        {
            var client = httpFactory.CreateClient(HttpClientName);
            using var content = new FormUrlEncodedContent(form);
            using var response = await client.PostAsync(s.VerifyUrl, content, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Turnstile answered {Status}", (int)response.StatusCode);
                return false;
            }

            var result = await response.Content.ReadFromJsonAsync<TurnstileResponse>(ct);
            if (result is null || !result.Success)
            {
                logger.LogInformation("Turnstile rejected the token: {Errors}", string.Join(',', result?.ErrorCodes ?? []));
                return false;
            }

            if (s.ExpectedHostnames.Length > 0 &&
                !s.ExpectedHostnames.Contains(result.Hostname ?? "", StringComparer.OrdinalIgnoreCase))
            {
                logger.LogWarning("Unexpected Turnstile hostname: {Host}", result.Hostname);
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "Could not verify Turnstile; rejecting (fail closed)");
            return false;
        }
    }

    internal sealed record TurnstileResponse(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("hostname")] string? Hostname,
        [property: JsonPropertyName("error-codes")] string[]? ErrorCodes);
}

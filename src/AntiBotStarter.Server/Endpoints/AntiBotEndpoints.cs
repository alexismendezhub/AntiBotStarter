using AntiBotStarter.Application.Configuration;
using AntiBotStarter.Application.Forms;
using AntiBotStarter.Application.Security;
using AntiBotStarter.Server.Security;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.Options;

namespace AntiBotStarter.Server.Endpoints;

public sealed record ClientConfigDto(bool CaptchaEnabled, string TurnstileSiteKey);

public static class AntiBotEndpoints
{
    public static IEndpointRouteBuilder MapAntiBotEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/robots.txt", (IOptionsMonitor<AntiBotOptions> o) =>
            Results.Text(RobotsTxtBuilder.Build(o.CurrentValue), "text/plain; charset=utf-8"));

        app.MapGet("/health", () => Results.Ok(new { status = "ok" })).DisableRateLimiting();

        var api = app.MapGroup("/api");

        // Public config for Angular (the Turnstile site key is public; the secret NEVER leaves the server).
        api.MapGet("/config", (IOptionsMonitor<AntiBotOptions> o) =>
            Results.Ok(new ClientConfigDto(o.CurrentValue.Captcha.Enabled, o.CurrentValue.Captcha.SiteKey)));

        api.MapGet("/antiforgery", AntiforgerySetup.IssueTokens);

        api.MapGet("/forms/token", (string? purpose, FormTokenService tokens) =>
                purpose is not null && FormPurposes.All.Contains(purpose)
                    ? Results.Ok(new { token = tokens.Issue(purpose) })
                    : Results.Json(new { error = "invalid_purpose" }, statusCode: StatusCodes.Status400BadRequest))
            .RequireRateLimiting(RateLimitPolicies.Api);

        return app;
    }
}

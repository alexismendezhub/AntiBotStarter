using AntiBotStarter.Application.Configuration;
using AntiBotStarter.Application.Forms;
using AntiBotStarter.Server.Security;
using Microsoft.Extensions.Options;

namespace AntiBotStarter.Server.Forms;

/// <summary>Contract of the protected forms.</summary>
public interface IProtectedForm
{
    /// <summary>Honeypot: invisible field. Humans leave it empty; many bots fill it in.</summary>
    string? Website { get; }
    string? FormToken { get; }
    string? CaptchaToken { get; }
}

/// <summary>
/// Endpoint filter that applies, in order: honeypot → form token (timing/single use) → CAPTCHA.
/// Honeypot/token errors are generic so the bot does not learn what failed.
/// </summary>
public sealed class FormProtectionFilter(string purpose) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var services = http.RequestServices;
        var events = services.GetRequiredService<SecurityEvents>();
        var rejected = Results.Json(new { error = "request_rejected", message = "The request could not be processed. Reload the page and try again." },
            statusCode: StatusCodes.Status400BadRequest);

        var form = context.Arguments.OfType<IProtectedForm>().FirstOrDefault();
        if (form is null)
        {
            events.Record("form_missing", http);
            return rejected;
        }

        if (!string.IsNullOrEmpty(form.Website))
        {
            events.Record("honeypot", http, purpose);
            return rejected;
        }

        var tokenResult = await services.GetRequiredService<FormTokenService>().ValidateAndConsumeAsync(form.FormToken, purpose);
        if (tokenResult != FormTokenResult.Valid)
        {
            events.Record("form_token", http, tokenResult.ToString());
            return rejected;
        }

        var captcha = services.GetRequiredService<IOptionsMonitor<AntiBotOptions>>().CurrentValue.Captcha;
        if (captcha.Enabled)
        {
            var ok = await services.GetRequiredService<ICaptchaVerifier>()
                .VerifyAsync(form.CaptchaToken, ClientIp.Address(http), http.RequestAborted);
            if (!ok)
            {
                events.Record("captcha_failed", http, purpose);
                return Results.Json(new { error = "captcha_failed", message = "We could not verify that you are human. Please try again." },
                    statusCode: StatusCodes.Status400BadRequest);
            }
        }

        return await next(context);
    }
}

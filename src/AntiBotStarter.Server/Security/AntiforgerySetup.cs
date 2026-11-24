using Microsoft.AspNetCore.Antiforgery;

namespace AntiBotStarter.Server.Security;

/// <summary>
/// Antiforgery for a SPA: the server issues an HttpOnly cookie (__Host-abs-af) and a cookie
/// readable by JS (XSRF-TOKEN). Angular HttpClient copies XSRF-TOKEN into the X-XSRF-TOKEN header
/// on every POST. A bot that does not run the site's full flow does not have a valid pair.
/// </summary>
public static class AntiforgerySetup
{
    public const string HeaderName = "X-XSRF-TOKEN";
    public const string RequestTokenCookie = "XSRF-TOKEN";

    public static IServiceCollection AddAntiBotAntiforgery(this IServiceCollection services)
    {
        services.AddAntiforgery(o =>
        {
            o.HeaderName = HeaderName;
            o.Cookie.Name = "__Host-abs-af";
            o.Cookie.HttpOnly = true;
            o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            o.Cookie.SameSite = SameSiteMode.Strict;
        });
        return services;
    }

    public static IResult IssueTokens(HttpContext ctx, IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(ctx);
        ctx.Response.Cookies.Append(RequestTokenCookie, tokens.RequestToken!, new CookieOptions
        {
            HttpOnly = false, // Angular needs to read it
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/"
        });
        return Results.NoContent();
    }
}

/// <summary>Endpoint filter: validates antiforgery on JSON endpoints (Minimal APIs do not do it on their own).</summary>
public sealed class AntiforgeryValidationFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var antiforgery = http.RequestServices.GetRequiredService<IAntiforgery>();
        try
        {
            await antiforgery.ValidateRequestAsync(http);
        }
        catch (AntiforgeryValidationException)
        {
            http.RequestServices.GetRequiredService<SecurityEvents>().Record("antiforgery_failed", http);
            return Results.Json(new { error = "antiforgery_failed" }, statusCode: StatusCodes.Status400BadRequest);
        }
        return await next(context);
    }
}

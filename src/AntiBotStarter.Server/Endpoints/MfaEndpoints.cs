using System.Text;
using AntiBotStarter.Server.Security;
using Microsoft.AspNetCore.Identity;

namespace AntiBotStarter.Server.Endpoints;

public sealed record MfaCodeRequest(string? Code);

/// <summary>
/// TOTP two-factor authentication (Microsoft/Google Authenticator, 1Password…) with recovery codes.
/// Every change requires a valid current code, is rate limited and antiforgery-protected, and revokes
/// the other sessions (security stamp).
/// </summary>
public static class MfaEndpoints
{
    public const string Issuer = "AntiBotStarter";
    private const int RecoveryCodeCount = 10;

    public static IEndpointRouteBuilder MapMfaEndpoints(this IEndpointRouteBuilder app)
    {
        var mfa = app.MapGroup("/api/account/mfa").RequireAuthorization();

        mfa.MapGet("/", StatusAsync).RequireRateLimiting(RateLimitPolicies.Api);

        mfa.MapPost("/setup", SetupAsync)
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .AddEndpointFilter<AntiforgeryValidationFilter>();
        mfa.MapPost("/enable", EnableAsync)
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .AddEndpointFilter<AntiforgeryValidationFilter>();
        mfa.MapPost("/disable", DisableAsync)
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .AddEndpointFilter<AntiforgeryValidationFilter>();
        mfa.MapPost("/recovery-codes", RegenerateRecoveryCodesAsync)
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .AddEndpointFilter<AntiforgeryValidationFilter>();

        return app;
    }

    private static async Task<IResult> StatusAsync(HttpContext ctx, UserManager<IdentityUser> users)
    {
        var user = await users.GetUserAsync(ctx.User);
        if (user is null) return Results.Unauthorized();
        return Results.Ok(new
        {
            enabled = await users.GetTwoFactorEnabledAsync(user),
            recoveryCodesLeft = await users.CountRecoveryCodesAsync(user)
        });
    }

    /// <summary>Creates a new authenticator key (only while 2FA is off) and returns it with its otpauth:// URI for the QR code.</summary>
    private static async Task<IResult> SetupAsync(HttpContext ctx, UserManager<IdentityUser> users)
    {
        var user = await users.GetUserAsync(ctx.User);
        if (user is null) return Results.Unauthorized();
        if (await users.GetTwoFactorEnabledAsync(user))
            return Results.Json(new { error = "already_enabled" }, statusCode: StatusCodes.Status409Conflict);

        await users.ResetAuthenticatorKeyAsync(user);
        var key = await users.GetAuthenticatorKeyAsync(user) ?? "";
        var email = await users.GetEmailAsync(user) ?? "";
        return Results.Ok(new { sharedKey = FormatKey(key), authenticatorUri = AuthenticatorUri(email, key) });
    }

    private static async Task<IResult> EnableAsync(MfaCodeRequest req, HttpContext ctx, UserManager<IdentityUser> users,
        SignInManager<IdentityUser> signIn, SecurityEvents events)
    {
        var user = await users.GetUserAsync(ctx.User);
        if (user is null) return Results.Unauthorized();
        if (await users.GetTwoFactorEnabledAsync(user))
            return Results.Json(new { error = "already_enabled" }, statusCode: StatusCodes.Status409Conflict);
        if (!await VerifyAsync(users, user, req.Code)) return InvalidCode(ctx, events);

        await users.SetTwoFactorEnabledAsync(user, true);
        var codes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount);
        await signIn.RefreshSignInAsync(user);
        events.Record("2fa_enabled", ctx);
        return Results.Ok(new { recoveryCodes = codes });
    }

    private static async Task<IResult> DisableAsync(MfaCodeRequest req, HttpContext ctx, UserManager<IdentityUser> users,
        SignInManager<IdentityUser> signIn, SecurityEvents events)
    {
        var user = await users.GetUserAsync(ctx.User);
        if (user is null) return Results.Unauthorized();
        if (!await users.GetTwoFactorEnabledAsync(user))
            return Results.Json(new { error = "not_enabled" }, statusCode: StatusCodes.Status409Conflict);
        if (!await VerifyAsync(users, user, req.Code)) return InvalidCode(ctx, events);

        await users.SetTwoFactorEnabledAsync(user, false);
        await users.ResetAuthenticatorKeyAsync(user);
        await signIn.RefreshSignInAsync(user);
        events.Record("2fa_disabled", ctx);
        return Results.Ok(new { enabled = false });
    }

    private static async Task<IResult> RegenerateRecoveryCodesAsync(MfaCodeRequest req, HttpContext ctx, UserManager<IdentityUser> users,
        SecurityEvents events)
    {
        var user = await users.GetUserAsync(ctx.User);
        if (user is null) return Results.Unauthorized();
        if (!await users.GetTwoFactorEnabledAsync(user))
            return Results.Json(new { error = "not_enabled" }, statusCode: StatusCodes.Status409Conflict);
        if (!await VerifyAsync(users, user, req.Code)) return InvalidCode(ctx, events);

        var codes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount);
        events.Record("2fa_recovery_codes_regenerated", ctx);
        return Results.Ok(new { recoveryCodes = codes });
    }

    private static async Task<bool> VerifyAsync(UserManager<IdentityUser> users, IdentityUser user, string? rawCode)
    {
        var code = CleanCode(rawCode);
        if (code is null) return false;
        var ok = await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, code);
        if (!ok)
        {
            // Wrong codes on these endpoints count towards the lockout too.
            await users.AccessFailedAsync(user);
        }
        return ok;
    }

    private static IResult InvalidCode(HttpContext ctx, SecurityEvents events)
    {
        events.Record("2fa_invalid_code", ctx);
        return Results.Json(new { error = "invalid_code" }, statusCode: StatusCodes.Status400BadRequest);
    }

    /// <summary>Accepts "123 456" or "123-456"; returns null unless it is exactly 6 digits.</summary>
    public static string? CleanCode(string? raw)
    {
        var code = raw?.Replace(" ", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal).Trim();
        return code is { Length: 6 } && code.All(char.IsAsciiDigit) ? code : null;
    }

    public static string AuthenticatorUri(string email, string key) =>
        $"otpauth://totp/{Uri.EscapeDataString(Issuer)}:{Uri.EscapeDataString(email)}?secret={key}&issuer={Uri.EscapeDataString(Issuer)}&digits=6";

    private static string FormatKey(string key)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < key.Length; i += 4)
            sb.Append(key.AsSpan(i, Math.Min(4, key.Length - i))).Append(' ');
        return sb.ToString().TrimEnd().ToLowerInvariant();
    }
}

using System.Text;
using AntiBotStarter.Application.Email;
using AntiBotStarter.Application.Forms;
using AntiBotStarter.Infrastructure.Email;
using AntiBotStarter.Server.Forms;
using AntiBotStarter.Server.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace AntiBotStarter.Server.Endpoints;

public sealed record RegisterRequest(string? Email, string? Password, string? Website, string? FormToken, string? CaptchaToken) : IProtectedForm;
public sealed record LoginRequest(string? Email, string? Password, bool RememberMe, string? Website, string? FormToken, string? CaptchaToken) : IProtectedForm;
public sealed record ConfirmEmailRequest(string? UserId, string? Code);
public sealed record EmailOnlyRequest(string? Email, string? Website, string? FormToken, string? CaptchaToken) : IProtectedForm;
public sealed record ResetPasswordRequest(string? Email, string? Code, string? NewPassword, string? Website, string? FormToken, string? CaptchaToken) : IProtectedForm;
public sealed record TwoFactorLoginRequest(string? Code, bool RememberMachine);
public sealed record RecoveryCodeLoginRequest(string? RecoveryCode);

public static class AuthEndpoints
{
    private const string GenericLoginMessage =
        "Wrong email or password, account temporarily locked, or email not confirmed.";
    private const string GenericEmailMessage =
        "If the address belongs to an account that needs it, you will receive an email shortly.";

    private static readonly IdentityUser DummyUser = new() { UserName = "timing-dummy" };
    private static string? _dummyHash;

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth");

        auth.MapPost("/register", RegisterAsync).AsProtectedForm(FormPurposes.Register);
        auth.MapPost("/login", LoginAsync).AsProtectedForm(FormPurposes.Login);
        auth.MapPost("/resend-confirmation", ResendConfirmationAsync).AsProtectedForm(FormPurposes.ResendConfirmation);
        auth.MapPost("/forgot-password", ForgotPasswordAsync).AsProtectedForm(FormPurposes.ForgotPassword);
        auth.MapPost("/reset-password", ResetPasswordAsync).AsProtectedForm(FormPurposes.ResetPassword);

        // Second login step: the password was already checked (with CAPTCHA); Identity's lockout counts wrong codes.
        auth.MapPost("/login-2fa", TwoFactorLoginAsync)
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .AddEndpointFilter<AntiforgeryValidationFilter>();
        auth.MapPost("/login-recovery", RecoveryCodeLoginAsync)
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .AddEndpointFilter<AntiforgeryValidationFilter>();

        auth.MapPost("/confirm-email", ConfirmEmailAsync)
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .AddEndpointFilter<AntiforgeryValidationFilter>();

        auth.MapPost("/logout", async (SignInManager<IdentityUser> signIn) =>
            {
                await signIn.SignOutAsync();
                return Results.NoContent();
            })
            .RequireAuthorization()
            .AddEndpointFilter<AntiforgeryValidationFilter>();

        auth.MapGet("/me", (HttpContext ctx) => Results.Ok(new { email = ctx.User.Identity?.Name }))
            .RequireAuthorization();

        return app;
    }

    /// <summary>Every public form: strict rate limit → antiforgery → honeypot + form token → CAPTCHA.</summary>
    private static RouteHandlerBuilder AsProtectedForm(this RouteHandlerBuilder builder, string purpose) =>
        builder.RequireRateLimiting(RateLimitPolicies.Auth)
            .AddEndpointFilter<AntiforgeryValidationFilter>()
            .AddEndpointFilter(new FormProtectionFilter(purpose));

    private static IResult Accepted(string message) => Results.Accepted(value: new { message });

    private static async Task<IResult> RegisterAsync(
        RegisterRequest req,
        UserManager<IdentityUser> users,
        IEmailSender<IdentityUser> emailSender,
        DisposableDomains disposable,
        IOptionsMonitor<EmailOptions> emailOptions,
        IConfiguration config,
        SecurityEvents events,
        HttpContext ctx)
    {
        // Same response whether or not the email exists → accounts cannot be enumerated.
        var accepted = Accepted("If the details are valid, you will receive an email to confirm your account.");

        var email = EmailOutbox.Normalize(req.Email);
        if (email is null)
            return Results.Json(new { error = "invalid_email" }, statusCode: StatusCodes.Status400BadRequest);
        if (emailOptions.CurrentValue.BlockDisposableDomains && disposable.IsDisposable(email))
        {
            events.Record("disposable_email", ctx);
            return Results.Json(new { error = "disposable_email", message = "Please use a permanent email address." },
                statusCode: StatusCodes.Status400BadRequest);
        }
        if (string.IsNullOrEmpty(req.Password) || req.Password.Length > 256)
            return Results.Json(new { error = "invalid_password" }, statusCode: StatusCodes.Status400BadRequest);

        if (await users.FindByEmailAsync(email) is not null)
        {
            events.Record("register_existing_email", ctx);
            return accepted;
        }

        var user = new IdentityUser { UserName = email, Email = email };
        var result = await users.CreateAsync(user, req.Password);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(e => e.Code is "DuplicateEmail" or "DuplicateUserName")) return accepted;
            return Results.Json(new { error = "invalid_password", details = result.Errors.Select(e => e.Description) },
                statusCode: StatusCodes.Status400BadRequest);
        }

        await SendConfirmationAsync(users, emailSender, config, user);
        return accepted;
    }

    private static async Task<IResult> ResendConfirmationAsync(
        EmailOnlyRequest req,
        UserManager<IdentityUser> users,
        IEmailSender<IdentityUser> emailSender,
        IConfiguration config,
        SecurityEvents events,
        HttpContext ctx)
    {
        var email = EmailOutbox.Normalize(req.Email);
        if (email is not null && await users.FindByEmailAsync(email) is { } user && !user.EmailConfirmed)
            await SendConfirmationAsync(users, emailSender, config, user);
        else
            events.Record("resend_ignored", ctx);
        return Accepted(GenericEmailMessage);
    }

    private static async Task<IResult> ForgotPasswordAsync(
        EmailOnlyRequest req,
        UserManager<IdentityUser> users,
        IEmailSender<IdentityUser> emailSender,
        IConfiguration config,
        SecurityEvents events,
        HttpContext ctx)
    {
        var email = EmailOutbox.Normalize(req.Email);
        if (email is not null && await users.FindByEmailAsync(email) is { EmailConfirmed: true } user)
        {
            var token = await users.GeneratePasswordResetTokenAsync(user);
            var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
            var link = $"{BaseUrl(config)}/reset-password?email={Uri.EscapeDataString(email)}&code={code}";
            await emailSender.SendPasswordResetLinkAsync(user, email, link);
        }
        else
        {
            events.Record("forgot_ignored", ctx);
        }
        return Accepted(GenericEmailMessage);
    }

    private static async Task<IResult> ResetPasswordAsync(
        ResetPasswordRequest req,
        UserManager<IdentityUser> users,
        SecurityEvents events,
        HttpContext ctx)
    {
        var bad = Results.Json(new { error = "invalid_reset", message = "The reset link is invalid or has expired." },
            statusCode: StatusCodes.Status400BadRequest);

        var email = EmailOutbox.Normalize(req.Email);
        if (email is null || string.IsNullOrEmpty(req.Code) || req.Code.Length > 2048 ||
            string.IsNullOrEmpty(req.NewPassword) || req.NewPassword.Length > 256)
            return bad;

        var user = await users.FindByEmailAsync(email);
        if (user is null)
        {
            events.Record("reset_unknown_user", ctx);
            return bad;
        }

        string token;
        try { token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(req.Code)); }
        catch (FormatException) { return bad; }

        var result = await users.ResetPasswordAsync(user, token, req.NewPassword);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(e => e.Code == "InvalidToken"))
            {
                events.Record("reset_invalid_token", ctx);
                return bad;
            }
            return Results.Json(new { error = "invalid_password", details = result.Errors.Select(e => e.Description) },
                statusCode: StatusCodes.Status400BadRequest);
        }

        // The owner proved control of the mailbox: clear a lockout caused by an attacker guessing the password.
        await users.SetLockoutEndDateAsync(user, null);
        await users.ResetAccessFailedCountAsync(user);
        events.Record("password_reset", ctx);
        return Results.Ok(new { reset = true });
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest req,
        UserManager<IdentityUser> users,
        SignInManager<IdentityUser> signIn,
        IPasswordHasher<IdentityUser> hasher,
        SecurityEvents events,
        HttpContext ctx)
    {
        var fail = Results.Json(new { error = "invalid_credentials", message = GenericLoginMessage },
            statusCode: StatusCodes.Status401Unauthorized);

        var email = req.Email?.Trim();
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(req.Password) || req.Password.Length > 256)
            return fail;

        var user = await users.FindByEmailAsync(email);
        if (user is null)
        {
            // Same CPU cost as a real user → no enumeration through response timing.
            _dummyHash ??= hasher.HashPassword(DummyUser, "Timing-Dummy-Password-1!");
            hasher.VerifyHashedPassword(DummyUser, _dummyHash, req.Password);
            events.Record("login_unknown_user", ctx);
            return fail;
        }

        var result = await signIn.PasswordSignInAsync(user, req.Password, req.RememberMe, lockoutOnFailure: true);
        if (result.Succeeded) return Results.Ok(new { email = user.Email });
        if (result.RequiresTwoFactor) return Results.Ok(new { requiresTwoFactor = true });

        events.Record(result.IsLockedOut ? "login_locked_out" : result.IsNotAllowed ? "login_not_allowed" : "login_failed", ctx);
        return fail;
    }

    private static async Task<IResult> TwoFactorLoginAsync(
        TwoFactorLoginRequest req,
        SignInManager<IdentityUser> signIn,
        SecurityEvents events,
        HttpContext ctx)
    {
        var code = MfaEndpoints.CleanCode(req.Code);
        if (code is null) return InvalidCode();

        var user = await signIn.GetTwoFactorAuthenticationUserAsync();
        if (user is null) return Results.Json(new { error = "two_factor_expired" }, statusCode: StatusCodes.Status401Unauthorized);

        var result = await signIn.TwoFactorAuthenticatorSignInAsync(code, isPersistent: false, rememberClient: req.RememberMachine);
        if (result.Succeeded) return Results.Ok(new { email = user.Email });

        events.Record(result.IsLockedOut ? "2fa_locked_out" : "2fa_failed", ctx);
        return InvalidCode();
    }

    private static async Task<IResult> RecoveryCodeLoginAsync(
        RecoveryCodeLoginRequest req,
        SignInManager<IdentityUser> signIn,
        SecurityEvents events,
        HttpContext ctx)
    {
        var code = req.RecoveryCode?.Replace(" ", "", StringComparison.Ordinal).Trim();
        if (string.IsNullOrEmpty(code) || code.Length > 64) return InvalidCode();

        var user = await signIn.GetTwoFactorAuthenticationUserAsync();
        if (user is null) return Results.Json(new { error = "two_factor_expired" }, statusCode: StatusCodes.Status401Unauthorized);

        var result = await signIn.TwoFactorRecoveryCodeSignInAsync(code);
        if (result.Succeeded)
        {
            events.Record("2fa_recovery_code_used", ctx);
            return Results.Ok(new { email = user.Email });
        }

        events.Record(result.IsLockedOut ? "2fa_locked_out" : "2fa_recovery_failed", ctx);
        return InvalidCode();
    }

    private static IResult InvalidCode() =>
        Results.Json(new { error = "invalid_code", message = "The code is not valid, or the account is temporarily locked." },
            statusCode: StatusCodes.Status401Unauthorized);

    private static async Task<IResult> ConfirmEmailAsync(ConfirmEmailRequest req, UserManager<IdentityUser> users)
    {
        var bad = Results.Json(new { error = "invalid_confirmation" }, statusCode: StatusCodes.Status400BadRequest);
        if (string.IsNullOrEmpty(req.UserId) || string.IsNullOrEmpty(req.Code) || req.Code.Length > 2048) return bad;

        var user = await users.FindByIdAsync(req.UserId);
        if (user is null) return bad;

        string token;
        try { token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(req.Code)); }
        catch (FormatException) { return bad; }

        var result = await users.ConfirmEmailAsync(user, token);
        return result.Succeeded ? Results.Ok(new { confirmed = true }) : bad;
    }

    private static async Task SendConfirmationAsync(UserManager<IdentityUser> users, IEmailSender<IdentityUser> emailSender,
        IConfiguration config, IdentityUser user)
    {
        var token = await users.GenerateEmailConfirmationTokenAsync(user);
        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var link = $"{BaseUrl(config)}/confirm-email?userId={Uri.EscapeDataString(user.Id)}&code={code}";
        await emailSender.SendConfirmationLinkAsync(user, user.Email!, link);
    }

    /// <summary>Base URL from configuration, NEVER from the Host header (prevents link poisoning).</summary>
    private static string BaseUrl(IConfiguration config) => (config["App:PublicBaseUrl"] ?? "").TrimEnd('/');
}

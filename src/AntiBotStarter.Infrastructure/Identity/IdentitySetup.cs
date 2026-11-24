using AntiBotStarter.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;

namespace AntiBotStarter.Infrastructure.Identity;

public static class IdentitySetup
{
    public static IServiceCollection AddAppIdentity(this IServiceCollection services)
    {
        services.AddIdentity<IdentityUser, IdentityRole>(o =>
            {
                // Strong passwords
                o.Password.RequiredLength = 12;
                o.Password.RequireDigit = true;
                o.Password.RequireLowercase = true;
                o.Password.RequireUppercase = true;
                o.Password.RequireNonAlphanumeric = true;
                o.Password.RequiredUniqueChars = 4;

                // Lockout after failed attempts (brute force), also for 2FA codes
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                o.Lockout.AllowedForNewUsers = true;

                // Slows down mass registration of fake accounts
                o.User.RequireUniqueEmail = true;
                o.SignIn.RequireConfirmedEmail = true;
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders()
            .AddPasswordValidator<PwnedPasswordValidator>();

        services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(24));

        // A password reset or MFA change revokes the other sessions within 5 minutes (default 30).
        services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(5));

        services.ConfigureApplicationCookie(o =>
        {
            o.Cookie.Name = "__Host-abs-auth";
            HardenCookie(o);
            o.ExpireTimeSpan = TimeSpan.FromHours(8);
            o.SlidingExpiration = true;
            // API: 401/403 instead of redirecting to a login page
            o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
            o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
        });

        // Cookie between the password step and the 2FA step, and the "remember this device" cookie.
        services.Configure<CookieAuthenticationOptions>(IdentityConstants.TwoFactorUserIdScheme, o =>
        {
            o.Cookie.Name = "__Host-abs-2fa";
            HardenCookie(o);
            o.ExpireTimeSpan = TimeSpan.FromMinutes(5);
        });
        services.Configure<CookieAuthenticationOptions>(IdentityConstants.TwoFactorRememberMeScheme, o =>
        {
            o.Cookie.Name = "__Host-abs-2fa-rm";
            HardenCookie(o);
            o.ExpireTimeSpan = TimeSpan.FromDays(30);
        });

        services.AddAuthorization();
        return services;
    }

    private static void HardenCookie(CookieAuthenticationOptions o)
    {
        o.Cookie.HttpOnly = true;
        o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        o.Cookie.SameSite = SameSiteMode.Strict;
        o.Cookie.Path = "/";
    }
}

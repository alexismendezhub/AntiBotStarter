using AntiBotStarter.Application.Configuration;
using AntiBotStarter.Application.Email;
using AntiBotStarter.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace AntiBotStarter.Server;

/// <summary>
/// In Production the app does NOT start with an insecure configuration.
/// A failed deployment is better than a site silently open to bots.
/// </summary>
public static class StartupValidation
{
    public static IReadOnlyList<string> Check(IServiceProvider services, IConfiguration configuration)
    {
        var o = services.GetRequiredService<IOptions<AntiBotOptions>>().Value;
        var email = services.GetRequiredService<IOptions<EmailOptions>>().Value;
        var db = services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        var dist = services.GetRequiredService<IOptions<DistributedOptions>>().Value;
        var dp = services.GetRequiredService<IOptions<DataProtectionSettings>>().Value;
        var errors = new List<string>();

        if (!o.Captcha.Enabled)
            errors.Add("AntiBot:Captcha:Enabled must be true in production.");
        if (o.Captcha.Enabled && (string.IsNullOrWhiteSpace(o.Captcha.SecretKey) || o.Captcha.SecretKey.StartsWith("1x0000", StringComparison.Ordinal)
                                  || o.Captcha.SecretKey.StartsWith("2x0000", StringComparison.Ordinal) || o.Captcha.SecretKey.StartsWith("3x0000", StringComparison.Ordinal)))
            errors.Add("AntiBot:Captcha:SecretKey is empty or the Turnstile test key. Use Key Vault / environment variables.");
        if (!o.BotFilter.Enabled)
            errors.Add("AntiBot:BotFilter:Enabled must be true in production.");
        if (string.IsNullOrWhiteSpace(configuration["App:PublicBaseUrl"]) ||
            !configuration["App:PublicBaseUrl"]!.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            errors.Add("App:PublicBaseUrl must be the public https URL of the site.");
        if (configuration["AllowedHosts"] is null or "*")
            errors.Add("AllowedHosts must list your domains (not '*') to enable host filtering.");

        // Email
        if (email.Transport is not ("Smtp" or "AzureCommunicationServices"))
            errors.Add("Email:Transport must be Smtp or AzureCommunicationServices (Log only writes to the log).");
        if (string.IsNullOrWhiteSpace(email.FromAddress))
            errors.Add("Email:FromAddress is required.");
        if (!email.BlockDisposableDomains)
            errors.Add("Email:BlockDisposableDomains must be true in production.");
        if (email.Transport == "Smtp" && (string.IsNullOrWhiteSpace(email.Smtp.Host) || email.Smtp.Security == "None"))
            errors.Add("Email:Smtp needs a Host and an encrypted connection (StartTls or SslOnConnect).");
        if (email.Transport == "AzureCommunicationServices" &&
            string.IsNullOrWhiteSpace(email.AzureCommunicationServices.Endpoint) && string.IsNullOrWhiteSpace(email.AzureCommunicationServices.ConnectionString))
            errors.Add("Email:AzureCommunicationServices needs an Endpoint (managed identity) or a ConnectionString.");

        // Data
        if (!string.Equals(db.Provider, "SqlServer", StringComparison.OrdinalIgnoreCase))
            errors.Add("Database:Provider must be SqlServer in production.");
        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("Default")))
            errors.Add("ConnectionStrings:Default is required.");
        if (db.MigrateOnStartup)
            errors.Add("Database:MigrateOnStartup must be false in production: run the migration bundle from the pipeline.");
        if (string.IsNullOrWhiteSpace(dp.KeyVaultKeyId))
            errors.Add("DataProtection:KeyVaultKeyId is required so the key ring is encrypted at rest.");
        if (!dist.UseRedis && !dist.SingleInstance)
            errors.Add("Set Distributed:RedisConnectionString (several instances) or Distributed:SingleInstance=true (exactly one instance).");

        return errors;
    }

    public static void Validate(WebApplication app)
    {
        if (!app.Environment.IsProduction()) return;
        var errors = Check(app.Services, app.Configuration);
        if (errors.Count > 0)
            throw new InvalidOperationException("Insecure configuration for production:" + Environment.NewLine + string.Join(Environment.NewLine, errors));
    }
}

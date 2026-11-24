using AntiBotStarter.Application.Forms;
using AntiBotStarter.Application.Security;
using AntiBotStarter.Infrastructure.Configuration;
using AntiBotStarter.Infrastructure.Data;
using AntiBotStarter.Infrastructure.Distributed;
using AntiBotStarter.Infrastructure.Email;
using AntiBotStarter.Infrastructure.Forms;
using AntiBotStarter.Infrastructure.Identity;
using AntiBotStarter.Infrastructure.Products;
using AntiBotStarter.Infrastructure.Security;
using Azure.Identity;
using Microsoft.AspNetCore.DataProtection;

namespace AntiBotStarter.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Database, Data Protection keys, Redis-shared state, Identity, email, CAPTCHA, DNS and the sample product store.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAppDatabase(configuration);

        // Data Protection keys sign cookies, antiforgery, form tokens and the email outbox.
        // They live in the shared database (every instance uses the same key ring) and, when
        // DataProtection:KeyVaultKeyId is set, are encrypted at rest with an Azure Key Vault key.
        services.AddOptions<DataProtectionSettings>().Bind(configuration.GetSection(DataProtectionSettings.Section));
        var dataProtection = services.AddDataProtection()
            .SetApplicationName("AntiBotStarter")
            .PersistKeysToDbContext<AppDbContext>();
        var keyVaultKeyId = configuration[$"{DataProtectionSettings.Section}:KeyVaultKeyId"];
        if (!string.IsNullOrWhiteSpace(keyVaultKeyId))
            dataProtection.ProtectKeysWithAzureKeyVault(new Uri(keyVaultKeyId), new DefaultAzureCredential());

        services.AddAntiBotDistributedState(configuration);
        services.AddAppIdentity();
        services.AddAppEmail(configuration);

        services.AddSingleton<IDnsResolver, SystemDnsResolver>();
        services.AddSingleton<ICaptchaVerifier, TurnstileCaptchaVerifier>();
        services.AddHttpClient(TurnstileCaptchaVerifier.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(5));
        services.AddHttpClient(PwnedPasswordValidator.HttpClientName, c =>
        {
            c.BaseAddress = new Uri("https://api.pwnedpasswords.com/");
            c.Timeout = TimeSpan.FromSeconds(5);
            c.DefaultRequestHeaders.Add("Add-Padding", "true");
            c.DefaultRequestHeaders.UserAgent.ParseAdd("AntiBotStarter/1.0");
        });

        services.AddSingleton<ProductStore>();
        return services;
    }
}

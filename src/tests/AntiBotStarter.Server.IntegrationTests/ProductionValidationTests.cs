using AntiBotStarter.Application.Configuration;
using AntiBotStarter.Application.Email;
using AntiBotStarter.Infrastructure.Configuration;
using AntiBotStarter.Server.IntegrationTests.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AntiBotStarter.Server.IntegrationTests;

public sealed class ProductionValidationTests
{
    /// <summary>Everything a real deployment sets (values are fake; nothing here is contacted).</summary>
    private static Dictionary<string, string?> SecureProduction() => new()
    {
        ["AllowedHosts"] = "www.example.com",
        ["App:PublicBaseUrl"] = "https://www.example.com",
        ["AntiBot:Captcha:SecretKey"] = "0x4AAAAAAAreal-secret",
        ["Database:Provider"] = "SqlServer",
        ["Database:MigrateOnStartup"] = "false",
        ["ConnectionStrings:Default"] = "Server=tcp:example.database.windows.net;Database=abs;Authentication=Active Directory Default",
        ["DataProtection:KeyVaultKeyId"] = "https://example.vault.azure.net/keys/dataprotection",
        ["Distributed:RedisConnectionString"] = "example.redis.cache.windows.net:6380,ssl=true",
        ["Email:Transport"] = "AzureCommunicationServices",
        ["Email:FromAddress"] = "no-reply@example.com",
        ["Email:AzureCommunicationServices:Endpoint"] = "https://example.communication.azure.com",
    };

    /// <summary>
    /// Binds the options exactly like the app does, without starting a host (a host would try to reach the
    /// fake Azure SQL / Key Vault above). Check() is the same method Production runs at startup.
    /// </summary>
    private static IReadOnlyList<string> Check(Dictionary<string, string?> settings)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddOptions<AntiBotOptions>().Bind(config.GetSection(AntiBotOptions.Section));
        services.AddOptions<EmailOptions>().Bind(config.GetSection(EmailOptions.Section));
        services.AddOptions<DatabaseOptions>().Bind(config.GetSection(DatabaseOptions.Section));
        services.AddOptions<DistributedOptions>().Bind(config.GetSection(DistributedOptions.Section));
        services.AddOptions<DataProtectionSettings>().Bind(config.GetSection(DataProtectionSettings.Section));
        using var sp = services.BuildServiceProvider();
        return StartupValidation.Check(sp, config);
    }

    [Fact]
    public void SecureProductionConfiguration_Passes()
    {
        var s = SecureProduction();
        Assert.Empty(Check(s));
    }

    [Theory]
    [InlineData("Email:Transport", "Log", "Email:Transport")]
    [InlineData("Email:FromAddress", "", "Email:FromAddress")]
    [InlineData("Email:BlockDisposableDomains", "false", "BlockDisposableDomains")]
    [InlineData("Database:Provider", "InMemory", "Database:Provider")]
    [InlineData("Database:MigrateOnStartup", "true", "MigrateOnStartup")]
    [InlineData("ConnectionStrings:Default", "", "ConnectionStrings:Default")]
    [InlineData("DataProtection:KeyVaultKeyId", "", "KeyVaultKeyId")]
    [InlineData("Distributed:RedisConnectionString", "", "Distributed:SingleInstance")]
    [InlineData("AntiBot:Captcha:SecretKey", "1x0000000000000000000000000000000AA", "SecretKey")]
    [InlineData("AllowedHosts", "*", "AllowedHosts")]
    [InlineData("App:PublicBaseUrl", "http://www.example.com", "PublicBaseUrl")]
    public void EachInsecureSetting_IsReported(string key, string value, string expected)
    {
        var s = SecureProduction();
        s[key] = value;
        Assert.Contains(Check(s), e => e.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void SingleInstance_IsAcceptedWithoutRedis()
    {
        var s = SecureProduction();
        s["Distributed:RedisConnectionString"] = "";
        s["Distributed:SingleInstance"] = "true";
        Assert.Empty(Check(s));
    }

    [Fact]
    public void SmtpWithoutEncryption_IsRejected()
    {
        var s = SecureProduction();
        s["Email:Transport"] = "Smtp";
        s["Email:Smtp:Host"] = "smtp.example.com";
        s["Email:Smtp:Security"] = "None";
        Assert.Contains(Check(s), e => e.Contains("Email:Smtp", StringComparison.Ordinal));
    }

    [Fact]
    public void ProductionHost_RefusesToStart()
    {
        using var f = new AntiBotFactory(environment: "Production");
        var ex = Assert.ThrowsAny<Exception>(() => f.CreateClient());
        Assert.Contains("Insecure configuration for production", ex.ToString());
    }
}

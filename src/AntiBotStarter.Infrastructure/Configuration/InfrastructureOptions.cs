namespace AntiBotStarter.Infrastructure.Configuration;

/// <summary>"Database" section.</summary>
public sealed class DatabaseOptions
{
    public const string Section = "Database";

    /// <summary>SqlServer (Azure SQL / SQL Server / LocalDB) or InMemory (tests only).</summary>
    public string Provider { get; set; } = "SqlServer";

    /// <summary>Name of the in-memory database (tests).</summary>
    public string Name { get; set; } = "antibot";

    /// <summary>
    /// Apply EF Core migrations at startup. Development only: in production run the migration bundle
    /// from the deployment pipeline, so several instances never race to migrate.
    /// </summary>
    public bool MigrateOnStartup { get; set; }
}

/// <summary>"DataProtection" section.</summary>
public sealed class DataProtectionSettings
{
    public const string Section = "DataProtection";

    /// <summary>
    /// Azure Key Vault key that encrypts the Data Protection key ring at rest
    /// (e.g. https://my-vault.vault.azure.net/keys/dataprotection). Required in Production.
    /// </summary>
    public string KeyVaultKeyId { get; set; } = "";
}

/// <summary>"Distributed" section: shared state for several instances.</summary>
public sealed class DistributedOptions
{
    public const string Section = "Distributed";

    /// <summary>When set, rate limits, the blocklist and single-use form-token nonces live in Redis.</summary>
    public string RedisConnectionString { get; set; } = "";

    /// <summary>Prefix of every Redis key, so several apps can share one Redis.</summary>
    public string KeyPrefix { get; set; } = "abs:";

    /// <summary>
    /// Production without Redis is only allowed when you explicitly state that a single instance runs
    /// (otherwise limits multiply per instance and a form token could be reused once per instance).
    /// </summary>
    public bool SingleInstance { get; set; }

    public bool UseRedis => !string.IsNullOrWhiteSpace(RedisConnectionString);
}

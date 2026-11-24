namespace AntiBotStarter.Application.Email;

/// <summary>"Email" section.</summary>
public sealed class EmailOptions
{
    public const string Section = "Email";

    /// <summary>Log (development: writes the message to the log), Smtp, or AzureCommunicationServices.</summary>
    public string Transport { get; set; } = "Log";

    public string FromAddress { get; set; } = "";
    public string FromName { get; set; } = "AntiBotStarter";

    /// <summary>Kill switch: messages are still queued (and throttled) but nothing is sent. Use it during an abuse incident.</summary>
    public bool Paused { get; set; }

    /// <summary>Rejects registrations from disposable email domains (embedded list + ExtraBlockedDomains).</summary>
    public bool BlockDisposableDomains { get; set; } = true;
    public string[] ExtraBlockedDomains { get; set; } = [];

    /// <summary>Sent/failed messages (already without body or recipient) are deleted after this many days.</summary>
    public int RetentionDays { get; set; } = 7;

    public EmailThrottleSettings Throttle { get; set; } = new();
    public EmailDispatcherSettings Dispatcher { get; set; } = new();
    public SmtpSettings Smtp { get; set; } = new();
    public AcsSettings AzureCommunicationServices { get; set; } = new();
}

/// <summary>
/// Abuse limits. They apply BEFORE a message is queued, whatever IP the request comes from,
/// so rotating IPs or solving CAPTCHAs cannot turn the site into a mail cannon.
/// </summary>
public sealed class EmailThrottleSettings
{
    /// <summary>Minimum minutes between two messages to the same address.</summary>
    public int PerRecipientMinMinutes { get; set; } = 15;

    /// <summary>Maximum messages per address in 24 hours.</summary>
    public int PerRecipientPerDay { get; set; } = 3;

    /// <summary>Maximum messages for the whole site per hour / per 24 hours (protects reputation and cost).</summary>
    public int GlobalPerHour { get; set; } = 200;
    public int GlobalPerDay { get; set; } = 1000;

    /// <summary>A warning is logged (alert on it) when the global usage reaches this percentage.</summary>
    public int GlobalWarningPercent { get; set; } = 80;
}

public sealed class EmailDispatcherSettings
{
    /// <summary>Background sending. Disabled in tests, which call RunOnceAsync directly.</summary>
    public bool Enabled { get; set; } = true;
    public int PollSeconds { get; set; } = 5;
    public int BatchSize { get; set; } = 20;
    public int MaxAttempts { get; set; } = 5;
    public int LockMinutes { get; set; } = 2;
}

public sealed class SmtpSettings
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;

    /// <summary>StartTls (587), SslOnConnect (465) or None (only for a local test server such as Mailpit).</summary>
    public string Security { get; set; } = "StartTls";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

public sealed class AcsSettings
{
    /// <summary>Connection string (development) …</summary>
    public string ConnectionString { get; set; } = "";

    /// <summary>… or the resource endpoint, authenticated with the app's managed identity (recommended in Azure).</summary>
    public string Endpoint { get; set; } = "";
}

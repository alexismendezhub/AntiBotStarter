namespace AntiBotStarter.Domain.Email;

public enum EmailStatus
{
    Pending = 0,
    Sending = 1,
    Sent = 2,
    Failed = 3
}

/// <summary>
/// Outbox row. The recipient and the body (which contains confirmation/reset tokens) are encrypted with
/// Data Protection and erased as soon as the message is sent or given up, so the table never keeps live tokens.
/// RecipientHash (SHA-256 of the normalized address) is what the throttling queries use.
/// </summary>
public sealed class EmailMessage
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = "";
    public string RecipientHash { get; set; } = "";
    public string? RecipientProtected { get; set; }
    public string Subject { get; set; } = "";
    public string? BodyProtected { get; set; }
    public EmailStatus Status { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset SendAfterUtc { get; set; }
    public DateTimeOffset? LockedUntilUtc { get; set; }
    public DateTimeOffset? SentUtc { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}

public static class EmailKinds
{
    public const string ConfirmEmail = "confirm-email";
    public const string ResetPassword = "reset-password";
}

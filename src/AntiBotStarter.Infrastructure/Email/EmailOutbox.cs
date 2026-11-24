using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AntiBotStarter.Application.Email;
using AntiBotStarter.Domain.Email;
using AntiBotStarter.Infrastructure.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AntiBotStarter.Infrastructure.Email;

/// <summary>
/// Queues messages in the database (outbox) after applying the abuse limits:
/// per-recipient cooldown and daily cap, site-wide hourly and daily caps, disposable domains.
/// The HTTP response never reveals whether a message was throttled (it is always the same generic answer).
/// Because the counters live in the shared database, the limits hold across instances and IPs.
/// </summary>
public sealed class EmailOutbox(
    AppDbContext db,
    IDataProtectionProvider dataProtection,
    TimeProvider time,
    IOptionsMonitor<EmailOptions> options,
    DisposableDomains disposable,
    EmailMetrics metrics,
    ILogger<EmailOutbox> logger) : IEmailOutbox
{
    public const string ProtectorPurpose = "AntiBotStarter.EmailOutbox.v1";

    public async Task<EnqueueResult> EnqueueAsync(string kind, string recipient, EmailContent content, CancellationToken ct = default)
    {
        var o = options.CurrentValue;
        var t = o.Throttle;
        var address = Normalize(recipient);
        if (address is null) return Reject(kind, EnqueueResult.InvalidAddress, "invalid_address");
        if (o.BlockDisposableDomains && disposable.IsDisposable(address))
            return Reject(kind, EnqueueResult.BlockedDisposableDomain, "disposable_domain");

        var hash = Hash(address);
        var now = time.GetUtcNow();

        var recipientLast = await db.EmailMessages.Where(m => m.RecipientHash == hash)
            .OrderByDescending(m => m.CreatedUtc).Select(m => (DateTimeOffset?)m.CreatedUtc).FirstOrDefaultAsync(ct);
        if (recipientLast is { } last && now - last < TimeSpan.FromMinutes(t.PerRecipientMinMinutes))
            return Reject(kind, EnqueueResult.ThrottledRecipientCooldown, "recipient_cooldown");

        var dayAgo = now.AddDays(-1);
        if (await db.EmailMessages.CountAsync(m => m.RecipientHash == hash && m.CreatedUtc > dayAgo, ct) >= t.PerRecipientPerDay)
            return Reject(kind, EnqueueResult.ThrottledRecipientDaily, "recipient_daily");

        var hourAgo = now.AddHours(-1);
        var lastHour = await db.EmailMessages.CountAsync(m => m.CreatedUtc > hourAgo, ct);
        if (lastHour >= t.GlobalPerHour)
            return Reject(kind, EnqueueResult.ThrottledGlobalHourly, "global_hourly", alert: true);

        var lastDay = await db.EmailMessages.CountAsync(m => m.CreatedUtc > dayAgo, ct);
        if (lastDay >= t.GlobalPerDay)
            return Reject(kind, EnqueueResult.ThrottledGlobalDaily, "global_daily", alert: true);

        if ((lastHour + 1) * 100 >= t.GlobalPerHour * t.GlobalWarningPercent || (lastDay + 1) * 100 >= t.GlobalPerDay * t.GlobalWarningPercent)
            logger.LogWarning("Email volume at {Hour}/{HourCap} per hour and {Day}/{DayCap} per day: check for abuse.",
                lastHour + 1, t.GlobalPerHour, lastDay + 1, t.GlobalPerDay);

        var protector = dataProtection.CreateProtector(ProtectorPurpose);
        db.EmailMessages.Add(new EmailMessage
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            RecipientHash = hash,
            RecipientProtected = protector.Protect(address),
            Subject = content.Subject,
            BodyProtected = protector.Protect(JsonSerializer.Serialize(new StoredBody(content.Html, content.Text))),
            Status = EmailStatus.Pending,
            CreatedUtc = now,
            SendAfterUtc = now
        });
        await db.SaveChangesAsync(ct);
        metrics.Queued(kind);
        return EnqueueResult.Queued;
    }

    private EnqueueResult Reject(string kind, EnqueueResult result, string reason, bool alert = false)
    {
        metrics.Throttled(kind, reason);
        if (alert)
            logger.LogError("Email {Reason} cap reached: no more messages are queued until the window passes. Possible abuse.", reason);
        else
            logger.LogInformation("Email not queued ({Reason}, kind {Kind}).", reason, kind);
        return result;
    }

    public static string? Normalize(string? email)
    {
        var e = email?.Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(e) || e.Length > 254 || !System.Net.Mail.MailAddress.TryCreate(e, out var a) || a.Address != e ? null : e;
    }

    public static string Hash(string normalizedAddress) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedAddress)));

    internal sealed record StoredBody(string Html, string Text);
}

using System.Text.Json;
using AntiBotStarter.Application.Email;
using AntiBotStarter.Domain.Email;
using AntiBotStarter.Infrastructure.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AntiBotStarter.Infrastructure.Email;

/// <summary>
/// Sends queued messages in the background.
///  - Claims each message with an optimistic-concurrency stamp, so two instances never send the same message.
///  - Retries with exponential backoff; gives up after MaxAttempts.
///  - Erases recipient and body as soon as a message is sent or given up; deletes old rows after RetentionDays.
///  - Honors Email:Paused (kill switch during an incident).
/// </summary>
public sealed class EmailDispatcher(
    IServiceScopeFactory scopes,
    IOptionsMonitor<EmailOptions> options,
    TimeProvider time,
    EmailMetrics metrics,
    ILogger<EmailDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.CurrentValue.Dispatcher.Enabled) return;

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, options.CurrentValue.Dispatcher.PollSeconds)), time);
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Email dispatcher iteration failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>One pass: purge, then claim and send a batch. Returns how many messages were sent.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        var o = options.CurrentValue;
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = time.GetUtcNow();

        await PurgeAsync(db, now, o, ct);
        if (o.Paused) return 0;

        var candidates = await db.EmailMessages
            .Where(m => (m.Status == EmailStatus.Pending && m.SendAfterUtc <= now) ||
                        (m.Status == EmailStatus.Sending && m.LockedUntilUtc < now))
            .OrderBy(m => m.CreatedUtc)
            .Take(Math.Max(1, o.Dispatcher.BatchSize))
            .ToListAsync(ct);

        var transport = scope.ServiceProvider.GetRequiredService<IEmailTransport>();
        var protector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector(EmailOutbox.ProtectorPurpose);
        var sent = 0;

        foreach (var message in candidates)
        {
            // Claim: if another instance changed the row first, the concurrency stamp makes SaveChanges fail.
            message.Status = EmailStatus.Sending;
            message.LockedUntilUtc = now.AddMinutes(o.Dispatcher.LockMinutes);
            message.ConcurrencyStamp = Guid.NewGuid();
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                db.Entry(message).State = EntityState.Detached;
                continue;
            }

            try
            {
                var body = JsonSerializer.Deserialize<EmailOutbox.StoredBody>(protector.Unprotect(message.BodyProtected!))!;
                var to = protector.Unprotect(message.RecipientProtected!);
                await transport.SendAsync(new OutgoingEmail(to, message.Subject, body.Html, body.Text), ct);

                message.Status = EmailStatus.Sent;
                message.SentUtc = time.GetUtcNow();
                Erase(message);
                metrics.Sent(message.Kind);
                sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                message.Attempts++;
                message.LastError = Truncate(ex.GetType().Name + ": " + ex.Message, 500);
                message.LockedUntilUtc = null;
                if (message.Attempts >= o.Dispatcher.MaxAttempts)
                {
                    message.Status = EmailStatus.Failed;
                    Erase(message);
                    metrics.Failed(message.Kind);
                    logger.LogError(ex, "Email {Id} ({Kind}) given up after {Attempts} attempts.", message.Id, message.Kind, message.Attempts);
                }
                else
                {
                    message.Status = EmailStatus.Pending;
                    message.SendAfterUtc = time.GetUtcNow().Add(Backoff(message.Attempts));
                    logger.LogWarning(ex, "Email {Id} ({Kind}) failed, attempt {Attempts}; retrying later.", message.Id, message.Kind, message.Attempts);
                }
            }

            message.ConcurrencyStamp = Guid.NewGuid();
            await db.SaveChangesAsync(ct);
        }

        return sent;
    }

    /// <summary>30 s, 1 min, 2 min, 4 min… capped at 30 min.</summary>
    public static TimeSpan Backoff(int attempts) =>
        TimeSpan.FromSeconds(Math.Min(1800, 30 * Math.Pow(2, Math.Max(0, attempts - 1))));

    private static void Erase(EmailMessage message)
    {
        message.BodyProtected = null;
        message.RecipientProtected = null;
        message.LockedUntilUtc = null;
    }

    private static async Task PurgeAsync(AppDbContext db, DateTimeOffset now, EmailOptions o, CancellationToken ct)
    {
        // Never shorter than 2 days: the 24-hour throttling windows count these rows.
        var cutoff = now.AddDays(-Math.Max(2, o.RetentionDays));
        var old = await db.EmailMessages
            .Where(m => (m.Status == EmailStatus.Sent || m.Status == EmailStatus.Failed) && m.CreatedUtc < cutoff)
            .Take(500).ToListAsync(ct);
        if (old.Count == 0) return;
        db.EmailMessages.RemoveRange(old);
        await db.SaveChangesAsync(ct);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}

using System.Net;
using AntiBotStarter.Application.Email;
using AntiBotStarter.Domain.Email;
using AntiBotStarter.Infrastructure.Email;
using AntiBotStarter.Server.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace AntiBotStarter.Server.IntegrationTests;

/// <summary>Abuse limits and safety of the real email pipeline (outbox + dispatcher).</summary>
public sealed class EmailProtectionTests
{
    private static AntiBotFactory Factory(params (string Key, string Value)[] settings) =>
        new(settings.ToDictionary(s => s.Key, s => (string?)s.Value), realEmail: true);

    private static async Task<HttpClient> ClientAsync(AntiBotFactory f, string ip = "198.51.100.10")
    {
        var c = f.CreateBrowserClient(ip);
        await c.PrimeAntiforgeryAsync();
        return c;
    }

    private static Task<int> CountAsync(AntiBotFactory f, Func<EmailMessage, bool>? filter = null) =>
        f.WithDbAsync(async db => (await db.EmailMessages.AsNoTracking().ToListAsync()).Count(filter ?? (_ => true)));

    [Fact]
    public async Task Register_QueuesEncryptedMessage_DispatcherSendsAndErasesIt()
    {
        using var f = Factory();
        var c = await ClientAsync(f);
        Assert.Equal(HttpStatusCode.Accepted, (await c.RegisterAsync("enc@example.com", ClientExtensions.StrongPassword)).StatusCode);

        var stored = await f.WithDbAsync(db => db.EmailMessages.AsNoTracking().SingleAsync());
        Assert.Equal(EmailStatus.Pending, stored.Status);
        Assert.DoesNotContain("enc@example.com", stored.RecipientProtected);
        Assert.DoesNotContain("confirm-email", stored.BodyProtected);
        Assert.Equal(EmailOutbox.Hash("enc@example.com"), stored.RecipientHash);

        Assert.Equal(1, await f.DispatchEmailsAsync());
        var sent = Assert.Single(f.Transport.Sent);
        Assert.Equal("enc@example.com", sent.To);
        Assert.Contains("https://localhost/confirm-email?userId=", sent.Text);
        Assert.Contains("href=\"https://localhost/confirm-email", sent.Html);

        var after = await f.WithDbAsync(db => db.EmailMessages.AsNoTracking().SingleAsync());
        Assert.Equal(EmailStatus.Sent, after.Status);
        Assert.Null(after.BodyProtected);
        Assert.Null(after.RecipientProtected);
    }

    [Fact]
    public async Task PerRecipientCooldown_RepeatedRequestsQueueOnlyOneMessage()
    {
        using var f = Factory();
        var c = await ClientAsync(f);
        await c.RegisterAsync("cool@example.com", ClientExtensions.StrongPassword);

        var first = await c.ResendConfirmationAsync("cool@example.com");
        var second = await c.ResendConfirmationAsync("cool@example.com");

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await second.Content.ReadAsStringAsync());
        Assert.Equal(1, await CountAsync(f));
    }

    [Fact]
    public async Task PerRecipientDailyCap_HoldsEvenFromDifferentIps()
    {
        using var f = Factory(("Email:Throttle:PerRecipientMinMinutes", "0"), ("Email:Throttle:PerRecipientPerDay", "2"));
        await (await ClientAsync(f, "203.0.113.60")).RegisterAsync("cap@example.com", ClientExtensions.StrongPassword);
        for (var i = 0; i < 4; i++)
            await (await ClientAsync(f, $"203.0.113.{61 + i}")).ResendConfirmationAsync("cap@example.com");

        Assert.Equal(2, await CountAsync(f));
    }

    [Fact]
    public async Task GlobalHourlyCap_StopsMassRegistrationOfThirdPartyAddresses()
    {
        using var f = Factory(("Email:Throttle:GlobalPerHour", "3"));
        for (var i = 0; i < 6; i++)
        {
            // A different victim address and a different IP every time: per-IP and per-recipient limits do not help.
            var res = await (await ClientAsync(f, $"203.0.113.{100 + i}")).RegisterAsync($"victim{i}@example.com", ClientExtensions.StrongPassword);
            Assert.Equal(HttpStatusCode.Accepted, res.StatusCode); // the attacker sees no difference
        }
        Assert.Equal(3, await CountAsync(f));
    }

    [Fact]
    public async Task GlobalDailyCap()
    {
        using var f = Factory(("Email:Throttle:GlobalPerDay", "2"));
        for (var i = 0; i < 4; i++)
            await (await ClientAsync(f, $"203.0.113.{120 + i}")).RegisterAsync($"day{i}@example.com", ClientExtensions.StrongPassword);
        Assert.Equal(2, await CountAsync(f));
    }

    [Theory]
    [InlineData("someone@mailinator.com")]
    [InlineData("someone@sub.mailinator.com")]
    [InlineData("someone@10minutemail.com")]
    [InlineData("someone@blocked-by-config.test")]
    public async Task DisposableDomains_AreRejectedAtRegistration(string email)
    {
        using var f = Factory(("Email:ExtraBlockedDomains:0", "blocked-by-config.test"));
        var res = await (await ClientAsync(f)).RegisterAsync(email, ClientExtensions.StrongPassword);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("disposable_email", await res.Content.ReadAsStringAsync());
        Assert.Equal(0, await CountAsync(f));
    }

    [Fact]
    public async Task Paused_QueuesButSendsNothing()
    {
        using var f = Factory(("Email:Paused", "true"));
        await (await ClientAsync(f)).RegisterAsync("paused@example.com", ClientExtensions.StrongPassword);
        Assert.Equal(0, await f.DispatchEmailsAsync());
        Assert.Empty(f.Transport.Sent);
        Assert.Equal(1, await CountAsync(f, m => m.Status == EmailStatus.Pending));
    }

    [Fact]
    public async Task ProviderOutage_RetriesWithBackoff_ThenGivesUpAndErases()
    {
        using var f = Factory(("Email:Dispatcher:MaxAttempts", "2"));
        f.Transport.FailuresToSimulate = 10;
        await (await ClientAsync(f)).RegisterAsync("outage@example.com", ClientExtensions.StrongPassword);

        Assert.Equal(0, await f.DispatchEmailsAsync());
        var retry = await f.WithDbAsync(db => db.EmailMessages.AsNoTracking().SingleAsync());
        Assert.Equal(EmailStatus.Pending, retry.Status);
        Assert.Equal(1, retry.Attempts);
        Assert.True(retry.SendAfterUtc > DateTimeOffset.UtcNow);
        Assert.Contains("simulated provider outage", retry.LastError);

        // Make it due again and fail the last attempt.
        await f.WithDbAsync(async db =>
        {
            var m = await db.EmailMessages.SingleAsync();
            m.SendAfterUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
            return await db.SaveChangesAsync();
        });
        await f.DispatchEmailsAsync();

        var failed = await f.WithDbAsync(db => db.EmailMessages.AsNoTracking().SingleAsync());
        Assert.Equal(EmailStatus.Failed, failed.Status);
        Assert.Null(failed.BodyProtected);
        Assert.Null(failed.RecipientProtected);
    }

    [Fact]
    public async Task OldMessages_ArePurged()
    {
        using var f = Factory(("Email:RetentionDays", "7"));
        await f.WithDbAsync(async db =>
        {
            db.EmailMessages.Add(new EmailMessage
            {
                Id = Guid.NewGuid(), Kind = EmailKinds.ConfirmEmail, RecipientHash = "X", Subject = "old",
                Status = EmailStatus.Sent, CreatedUtc = DateTimeOffset.UtcNow.AddDays(-10), SendAfterUtc = DateTimeOffset.UtcNow.AddDays(-10)
            });
            return await db.SaveChangesAsync();
        });
        await f.DispatchEmailsAsync();
        Assert.Equal(0, await CountAsync(f));
    }

    [Fact]
    public async Task Backoff_GrowsAndIsCapped()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), EmailDispatcher.Backoff(1));
        Assert.Equal(TimeSpan.FromSeconds(60), EmailDispatcher.Backoff(2));
        Assert.Equal(TimeSpan.FromMinutes(30), EmailDispatcher.Backoff(20));
        await Task.CompletedTask;
    }

    [Fact]
    public void Templates_EncodeTheLink_AndContainNoUserText()
    {
        var mail = EmailTemplates.ConfirmEmail("https://localhost/confirm-email?userId=1&code=<script>");
        Assert.DoesNotContain("<script>", mail.Html);
        Assert.Contains("&lt;script&gt;", mail.Html);
    }
}

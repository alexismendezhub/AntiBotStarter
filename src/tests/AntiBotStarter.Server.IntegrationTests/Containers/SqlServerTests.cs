using System.Net;
using AntiBotStarter.Application.Email;
using AntiBotStarter.Domain.Email;
using AntiBotStarter.Server.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AntiBotStarter.Server.IntegrationTests.Containers;

/// <summary>Real SQL Server 2022: migrations, the full account flow and the outbox under concurrency.</summary>
public sealed class SqlServerTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>
{
    private AntiBotFactory Factory(string database, bool realEmail = false) => new(new Dictionary<string, string?>
    {
        ["Database:Provider"] = "SqlServer",
        ["Database:MigrateOnStartup"] = "true",
        ["ConnectionStrings:Default"] = sql.ConnectionString(database),
    }, realEmail);

    [SkippableFact]
    public async Task Migrations_Apply_AndTheFullAccountFlowWorks()
    {
        sql.EnsureAvailable();
        using var f = Factory("abs_flow_" + Guid.NewGuid().ToString("N")[..8]);
        var c = f.CreateBrowserClient();

        await c.RegisterAndConfirmAsync(f, "sql@example.com");
        Assert.Equal(HttpStatusCode.OK, (await c.LoginAsync("sql@example.com", ClientExtensions.StrongPassword)).StatusCode);
        Assert.Contains("sql@example.com", await c.GetStringAsync("/api/auth/me"));

        var pending = await f.WithDbAsync(db => db.Database.GetPendingMigrationsAsync());
        Assert.Empty(pending);
        // The Data Protection key ring is stored in the shared database.
        Assert.True(await f.WithDbAsync(db => db.DataProtectionKeys.CountAsync()) > 0);
    }

    [SkippableFact]
    public async Task Outbox_TwoInstancesDispatchingTogether_SendEachMessageExactlyOnce()
    {
        sql.EnsureAvailable();
        var database = "abs_outbox_" + Guid.NewGuid().ToString("N")[..8];
        using var a = Factory(database, realEmail: true);
        using var b = Factory(database, realEmail: true);
        _ = a.Services; // A migrates first
        _ = b.Services;

        const int total = 30;
        await using (var scope = a.Services.CreateAsyncScope())
        {
            var outbox = scope.ServiceProvider.GetRequiredService<IEmailOutbox>();
            for (var i = 0; i < total; i++)
                Assert.Equal(EnqueueResult.Queued, await outbox.EnqueueAsync(EmailKinds.ConfirmEmail, $"user{i}@example.com",
                    EmailTemplates.ConfirmEmail($"https://localhost/confirm-email?n={i}")));
        }

        for (var round = 0; round < 5; round++)
            await Task.WhenAll(a.DispatchEmailsAsync(), b.DispatchEmailsAsync(), a.DispatchEmailsAsync(), b.DispatchEmailsAsync());

        var sent = a.Transport.Sent.Concat(b.Transport.Sent).Select(m => m.To).ToList();
        Assert.Equal(total, sent.Count);
        Assert.Equal(total, sent.Distinct().Count());
        Assert.Equal(total, await a.WithDbAsync(db => db.EmailMessages.CountAsync(m => m.Status == EmailStatus.Sent)));
    }

    [SkippableFact]
    public async Task ThrottlingCounters_AreSharedByInstances()
    {
        sql.EnsureAvailable();
        var database = "abs_throttle_" + Guid.NewGuid().ToString("N")[..8];
        using var a = Factory(database, realEmail: true);
        using var b = Factory(database, realEmail: true);
        _ = a.Services;

        async Task<EnqueueResult> Enqueue(AntiBotFactory f)
        {
            await using var scope = f.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IEmailOutbox>()
                .EnqueueAsync(EmailKinds.ResetPassword, "shared@example.com", EmailTemplates.ResetPassword("https://localhost/x"));
        }

        Assert.Equal(EnqueueResult.Queued, await Enqueue(a));
        Assert.Equal(EnqueueResult.ThrottledRecipientCooldown, await Enqueue(b)); // B sees A's message
    }

    [SkippableFact]
    public async Task ReadinessCheck_IsHealthyWithTheDatabase()
    {
        sql.EnsureAvailable();
        using var f = Factory("abs_health_" + Guid.NewGuid().ToString("N")[..8]);
        var res = await f.CreateBrowserClient().GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("Healthy", await res.Content.ReadAsStringAsync());
    }
}

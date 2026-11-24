using System.Net;
using System.Text.Json;
using AntiBotStarter.Domain.Email;
using AntiBotStarter.Server.IntegrationTests.Support;

namespace AntiBotStarter.Server.IntegrationTests.Containers;

/// <summary>Real SMTP delivery through MailKit into Mailpit, verified with Mailpit's HTTP API.</summary>
public sealed class SmtpTests(MailpitFixture mailpit) : IClassFixture<MailpitFixture>
{
    private static readonly HttpClient Http = new();

    private async Task<JsonElement> MessagesAsync() =>
        JsonDocument.Parse(await Http.GetStringAsync(new Uri(mailpit.ApiBase, "api/v1/messages"))).RootElement;

    [SkippableFact]
    public async Task Registration_EmailIsDeliveredOverSmtp_WithLinkAndBothBodies()
    {
        mailpit.EnsureAvailable();
        using var f = new AntiBotFactory(new Dictionary<string, string?>
        {
            ["Email:Transport"] = "Smtp",
            ["Email:FromAddress"] = "no-reply@antibotstarter.test",
            ["Email:Smtp:Host"] = mailpit.Host,
            ["Email:Smtp:Port"] = mailpit.MappedSmtpPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Email:Smtp:Security"] = "None",
        }, realEmail: true, captureTransport: false);

        var c = f.CreateBrowserClient();
        await c.PrimeAntiforgeryAsync();
        var address = $"smtp-{Guid.NewGuid():N}@example.com";
        Assert.Equal(HttpStatusCode.Accepted, (await c.RegisterAsync(address, ClientExtensions.StrongPassword)).StatusCode);
        Assert.Equal(1, await f.DispatchEmailsAsync());

        var messages = await MessagesAsync();
        var message = messages.GetProperty("messages").EnumerateArray()
            .Single(m => m.GetProperty("To").EnumerateArray().Any(t => t.GetProperty("Address").GetString() == address));
        Assert.Equal("Confirm your email", message.GetProperty("Subject").GetString());
        Assert.Equal("no-reply@antibotstarter.test", message.GetProperty("From").GetProperty("Address").GetString());

        var id = message.GetProperty("ID").GetString();
        var full = JsonDocument.Parse(await Http.GetStringAsync(new Uri(mailpit.ApiBase, $"api/v1/message/{id}"))).RootElement;
        Assert.Contains("https://localhost/confirm-email?userId=", full.GetProperty("Text").GetString());
        Assert.Contains("href=\"https://localhost/confirm-email", full.GetProperty("HTML").GetString());
    }

    [SkippableFact]
    public async Task UnreachableSmtpServer_IsRetriedNotLost()
    {
        mailpit.EnsureAvailable();
        using var f = new AntiBotFactory(new Dictionary<string, string?>
        {
            ["Email:Transport"] = "Smtp",
            ["Email:Smtp:Host"] = "127.0.0.1",
            ["Email:Smtp:Port"] = "1", // nothing listens there
            ["Email:Smtp:Security"] = "None",
        }, realEmail: true, captureTransport: false);

        var c = f.CreateBrowserClient();
        await c.PrimeAntiforgeryAsync();
        await c.RegisterAsync("retry@example.com", ClientExtensions.StrongPassword);
        Assert.Equal(0, await f.DispatchEmailsAsync());

        var row = await f.WithDbAsync(db => Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(db.EmailMessages));
        Assert.Equal(EmailStatus.Pending, row.Status);
        Assert.Equal(1, row.Attempts);
        Assert.NotNull(row.BodyProtected);
    }
}

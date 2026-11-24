using AntiBotStarter.Application.Email;
using AntiBotStarter.Domain.Email;
using Azure;
using Azure.Communication.Email;
using Azure.Identity;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace AntiBotStarter.Infrastructure.Email;

/// <summary>DEVELOPMENT ONLY: writes the plain-text message (with its link) to the log. StartupValidation blocks it in Production.</summary>
public sealed class LogEmailTransport(ILogger<LogEmailTransport> logger) : IEmailTransport
{
    public Task SendAsync(OutgoingEmail message, CancellationToken ct)
    {
        logger.LogInformation("[DEV EMAIL] To: {To} | {Subject}\n{Text}", message.To, message.Subject, message.Text);
        return Task.CompletedTask;
    }
}

/// <summary>SMTP through MailKit (SendGrid, Amazon SES, Mailgun, Postmark, Microsoft 365, ACS SMTP…).</summary>
public sealed class SmtpEmailTransport(IOptionsMonitor<EmailOptions> options) : IEmailTransport
{
    public async Task SendAsync(OutgoingEmail message, CancellationToken ct)
    {
        var o = options.CurrentValue;
        var s = o.Smtp;

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(o.FromName, o.FromAddress));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { HtmlBody = message.Html, TextBody = message.Text }.ToMessageBody();

        var security = s.Security switch
        {
            "SslOnConnect" => SecureSocketOptions.SslOnConnect,
            "None" => SecureSocketOptions.None,
            _ => SecureSocketOptions.StartTls
        };

        using var client = new SmtpClient();
        client.Timeout = 30_000;
        await client.ConnectAsync(s.Host, s.Port, security, ct);
        if (!string.IsNullOrEmpty(s.Username))
            await client.AuthenticateAsync(s.Username, s.Password, ct);
        await client.SendAsync(new SendRequest(mime), ct);
        await client.DisconnectAsync(true, ct);
    }
}

/// <summary>
/// Azure Communication Services Email. In Azure use Endpoint + the app's managed identity (no secret);
/// the connection string is for local development.
/// </summary>
public sealed class AcsEmailTransport : IEmailTransport
{
    private readonly EmailClient _client;
    private readonly IOptionsMonitor<EmailOptions> _options;

    public AcsEmailTransport(IOptionsMonitor<EmailOptions> options)
    {
        _options = options;
        var acs = options.CurrentValue.AzureCommunicationServices;
        _client = !string.IsNullOrWhiteSpace(acs.Endpoint)
            ? new EmailClient(new Uri(acs.Endpoint), new DefaultAzureCredential())
            : new EmailClient(acs.ConnectionString);
    }

    /// <summary>For tests (EmailClient is mockable).</summary>
    internal AcsEmailTransport(EmailClient client, IOptionsMonitor<EmailOptions> options)
    {
        _client = client;
        _options = options;
    }

    public async Task SendAsync(OutgoingEmail message, CancellationToken ct)
    {
        var content = new Azure.Communication.Email.EmailContent(message.Subject) { Html = message.Html, PlainText = message.Text };
        var email = new Azure.Communication.Email.EmailMessage(_options.CurrentValue.FromAddress, message.To, content);
        // WaitUntil.Started: ACS accepts the message; delivery is asynchronous on its side.
        await _client.SendAsync(WaitUntil.Started, email, ct);
    }
}

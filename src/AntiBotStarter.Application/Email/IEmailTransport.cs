using Microsoft.Extensions.Options;

namespace AntiBotStarter.Application.Email;

public sealed record OutgoingEmail(string To, string Subject, string Html, string Text);

public interface IEmailTransport
{
    Task SendAsync(OutgoingEmail message, CancellationToken ct);
}

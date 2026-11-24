using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace AntiBotStarter.Application.Email;

public enum EnqueueResult
{
    Queued,
    ThrottledRecipientCooldown,
    ThrottledRecipientDaily,
    ThrottledGlobalHourly,
    ThrottledGlobalDaily,
    BlockedDisposableDomain,
    InvalidAddress
}

public interface IEmailOutbox
{
    Task<EnqueueResult> EnqueueAsync(string kind, string recipient, EmailContent content, CancellationToken ct = default);
}

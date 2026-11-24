using AntiBotStarter.Application.Email;
using AntiBotStarter.Domain.Email;
using Microsoft.AspNetCore.Identity;

namespace AntiBotStarter.Infrastructure.Email;

/// <summary>
/// Identity's email sender: it only queues (with the abuse limits) and returns at once.
/// The response time does not depend on the email provider, and the throttling result is never exposed.
/// </summary>
public sealed class IdentityEmailSender(IEmailOutbox queue) : IEmailSender<IdentityUser>
{
    public Task SendConfirmationLinkAsync(IdentityUser user, string email, string confirmationLink) =>
        queue.EnqueueAsync(EmailKinds.ConfirmEmail, email, EmailTemplates.ConfirmEmail(confirmationLink));

    public Task SendPasswordResetLinkAsync(IdentityUser user, string email, string resetLink) =>
        queue.EnqueueAsync(EmailKinds.ResetPassword, email, EmailTemplates.ResetPassword(resetLink));

    /// <summary>Not used by this starter (it sends links, not codes).</summary>
    public Task SendPasswordResetCodeAsync(IdentityUser user, string email, string resetCode) =>
        throw new NotSupportedException("This starter sends reset links, not codes.");
}

using System.Text.Encodings.Web;

namespace AntiBotStarter.Application.Email;

public sealed record EmailContent(string Subject, string Html, string Text);

/// <summary>
/// Fixed templates. They contain ONLY our own text and a link built from App:PublicBaseUrl:
/// no user-supplied text ends up in a message, so the site cannot be used to send spam content.
/// </summary>
public static class EmailTemplates
{
    public static EmailContent ConfirmEmail(string link) => Build(
        "Confirm your email",
        "Confirm your email address to activate your account.",
        "Confirm email",
        link,
        "If you did not create an account, ignore this message.");

    public static EmailContent ResetPassword(string link) => Build(
        "Reset your password",
        "We received a request to reset your password. The link expires in 24 hours.",
        "Reset password",
        link,
        "If you did not ask for it, ignore this message: your password does not change.");

    private static EmailContent Build(string subject, string intro, string action, string link, string footer)
    {
        var href = HtmlEncoder.Default.Encode(link);
        var html =
            "<!doctype html><html><body style=\"font-family:Segoe UI,Arial,sans-serif;color:#1d2433\">" +
            $"<p>{HtmlEncoder.Default.Encode(intro)}</p>" +
            $"<p><a href=\"{href}\" style=\"background:#2b59c3;color:#fff;padding:10px 16px;border-radius:6px;text-decoration:none\">{HtmlEncoder.Default.Encode(action)}</a></p>" +
            $"<p style=\"font-size:12px;color:#5b6475\">{HtmlEncoder.Default.Encode(footer)}</p>" +
            "</body></html>";
        var text = $"{intro}\n\n{action}: {link}\n\n{footer}\n";
        return new EmailContent(subject, html, text);
    }
}

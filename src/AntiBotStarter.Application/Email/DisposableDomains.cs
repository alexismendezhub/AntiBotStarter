using System.Collections.Frozen;
using System.Reflection;
using Microsoft.Extensions.Options;

namespace AntiBotStarter.Application.Email;

/// <summary>
/// Disposable / temporary email domains. The embedded list comes from
/// github.com/disposable-email-domains/disposable-email-domains (CC0) and can be extended with
/// Email:ExtraBlockedDomains. Subdomains are blocked too (x.mailinator.com).
/// </summary>
public sealed class DisposableDomains
{
    private const string Resource = "AntiBotStarter.Application.Email.disposable_email_blocklist.conf";
    private readonly FrozenSet<string> _embedded;
    private readonly IOptionsMonitor<EmailOptions> _options;

    public DisposableDomains(IOptionsMonitor<EmailOptions> options)
    {
        _options = options;
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException($"Embedded resource {Resource} not found.");
        using var reader = new StreamReader(stream);
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.ReadLine() is { } line)
        {
            line = line.Trim();
            if (line.Length > 0 && !line.StartsWith('#')) set.Add(line);
        }
        _embedded = set.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }

    public int Count => _embedded.Count;

    public bool IsDisposable(string emailOrDomain)
    {
        var at = emailOrDomain.LastIndexOf('@');
        var domain = (at >= 0 ? emailOrDomain[(at + 1)..] : emailOrDomain).Trim().TrimEnd('.').ToLowerInvariant();
        var extra = _options.CurrentValue.ExtraBlockedDomains;

        while (domain.Length > 0)
        {
            if (_embedded.Contains(domain) || extra.Contains(domain, StringComparer.OrdinalIgnoreCase)) return true;
            var dot = domain.IndexOf('.');
            if (dot < 0) break;
            domain = domain[(dot + 1)..];
        }
        return false;
    }
}

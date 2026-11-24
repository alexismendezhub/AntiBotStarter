namespace AntiBotStarter.Application.Configuration;

/// <summary>
/// All anti-bot settings live in the "AntiBot" section of appsettings.
/// Arrays have no defaults in code (the configuration binder appends to them),
/// so the lists are defined in appsettings.json.
/// </summary>
public sealed class AntiBotOptions
{
    public const string Section = "AntiBot";

    /// <summary>When false: robots.txt "Disallow: /", X-Robots-Tag noindex, and Googlebot/Bingbot are blocked.</summary>
    public bool AllowSearchEngines { get; set; }

    /// <summary>Paths that skip the User-Agent filter and the blocklist (health probes, robots.txt).</summary>
    public string[] ExemptPaths { get; set; } = [];

    public string ContentSecurityPolicy { get; set; } = "default-src 'self'; object-src 'none'; frame-ancestors 'none'";

    public string[] AllowedCorsOrigins { get; set; } = [];

    public RateLimitSettings RateLimits { get; set; } = new();
    public BotFilterSettings BotFilter { get; set; } = new();
    public CrawlerTrapSettings CrawlerTrap { get; set; } = new();
    public FormProtectionSettings Forms { get; set; } = new();
    public CaptchaSettings Captcha { get; set; } = new();
    public OriginLockSettings OriginLock { get; set; } = new();
    public ForwardedHeadersSettings ForwardedHeaders { get; set; } = new();
    public ScrapingSettings Scraping { get; set; } = new();
    public PwnedPasswordSettings PwnedPasswords { get; set; } = new();
}

public sealed class RateLimitSettings
{
    /// <summary>Requests per minute per IP (sliding window) for the whole site.</summary>
    public int GlobalPermitPerMinute { get; set; } = 120;

    /// <summary>Requests per minute per IP for login/registration/confirmation.</summary>
    public int AuthPermitPerMinute { get; set; } = 5;

    /// <summary>Token bucket for the data API, per authenticated user or per IP.</summary>
    public int ApiTokenLimit { get; set; } = 30;
    public int ApiTokensPerPeriod { get; set; } = 10;
    public int ApiReplenishSeconds { get; set; } = 10;
}

public sealed class BotFilterSettings
{
    public bool Enabled { get; set; } = true;
    public bool BlockEmptyUserAgent { get; set; } = true;
    public bool BlockAiCrawlers { get; set; } = true;
    public string[] BlockedUserAgentFragments { get; set; } = [];
    public string[] AiCrawlerUserAgents { get; set; } = [];

    /// <summary>Tokens that only exist in robots.txt (they are not real User-Agents), e.g. Google-Extended.</summary>
    public string[] RobotsOnlyAiTokens { get; set; } = [];

    public List<SearchEngineRule> SearchEngines { get; set; } = [];
}

public sealed class SearchEngineRule
{
    public string UserAgentFragment { get; set; } = "";

    /// <summary>Valid DNS suffixes after the reverse lookup, e.g. ".googlebot.com".</summary>
    public string[] AllowedHostSuffixes { get; set; } = [];
}

public sealed class CrawlerTrapSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Trap path. It is Disallowed in robots.txt and linked from a link humans cannot see.</summary>
    public string Path { get; set; } = "/_internal/archive";

    public int BlockMinutes { get; set; } = 1440;
}

public sealed class FormProtectionSettings
{
    public int MinSecondsToSubmit { get; set; } = 3;
    public int MaxMinutesValid { get; set; } = 30;
}

public sealed class CaptchaSettings
{
    public bool Enabled { get; set; } = true;
    public string SiteKey { get; set; } = "";
    public string SecretKey { get; set; } = "";
    public string VerifyUrl { get; set; } = "https://challenges.cloudflare.com/turnstile/v0/siteverify";

    /// <summary>When set, the hostname returned by Turnstile must be in the list.</summary>
    public string[] ExpectedHostnames { get; set; } = [];
}

public sealed class OriginLockSettings
{
    /// <summary>Enable it when the site is behind Front Door / Cloudflare so nobody can reach the origin directly.</summary>
    public bool Enabled { get; set; }
    public string HeaderName { get; set; } = "X-Azure-FDID";
    public string[] ExpectedValues { get; set; } = [];
    public string[] ExemptPaths { get; set; } = [];
}

public sealed class ForwardedHeadersSettings
{
    /// <summary>IPs of your proxies/load balancers. X-Forwarded-For is only trusted from them.</summary>
    public string[] KnownProxies { get; set; } = [];

    /// <summary>CIDR networks of proxies, e.g. Cloudflare ranges.</summary>
    public string[] KnownNetworks { get; set; } = [];

    /// <summary>Behind Cloudflare you can use "CF-Connecting-IP".</summary>
    public string ForwardedForHeaderName { get; set; } = "X-Forwarded-For";

    public int ForwardLimit { get; set; } = 1;
}

public sealed class ScrapingSettings
{
    public bool RequireAuthForData { get; set; } = true;
    public int MaxPageSize { get; set; } = 50;

    /// <summary>Highest reachable page: prevents walking the whole catalog by paging.</summary>
    public int MaxPage { get; set; } = 20;
}

public sealed class PwnedPasswordSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>true = if the HIBP API does not respond, the password is rejected.</summary>
    public bool FailClosed { get; set; }
}

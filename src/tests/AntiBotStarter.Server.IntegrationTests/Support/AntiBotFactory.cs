using System.Collections.Concurrent;
using System.Net;
using AntiBotStarter.Application.Email;
using AntiBotStarter.Application.Forms;
using AntiBotStarter.Application.Security;
using AntiBotStarter.Infrastructure.Data;
using AntiBotStarter.Infrastructure.Email;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AntiBotStarter.Server.IntegrationTests.Support;

/// <summary>
/// Test host: in-memory DB, high limits by default (each test lowers the one it needs),
/// fake CAPTCHA/DNS/email and an X-Test-Client-IP header to simulate the client IP.
/// realEmail: false → Identity emails are captured directly (fast);
/// true → the real outbox (throttling, encryption) runs and the dispatcher sends to <see cref="Transport"/>.
/// </summary>
public sealed class AntiBotFactory : WebApplicationFactory<Program>
{
    public const string BrowserUa = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36";

    private readonly Dictionary<string, string?> _settings;

    public FakeCaptchaVerifier Captcha { get; } = new();
    public FakeDnsResolver Dns { get; } = new();
    public CapturingEmailSender Emails { get; } = new();
    public CapturingTransport Transport { get; } = new();
    private readonly bool _realEmail;

    private readonly bool _captureTransport;

    /// <param name="captureTransport">With realEmail, false keeps the configured transport (e.g. Smtp to Mailpit).</param>
    private readonly string _environment;

    public AntiBotFactory(IDictionary<string, string?>? overrides = null, bool realEmail = false, bool captureTransport = true,
        string environment = "Testing")
    {
        _environment = environment;
        _realEmail = realEmail;
        _captureTransport = captureTransport;
        _settings = new Dictionary<string, string?>
        {
            ["Database:Provider"] = "InMemory",
            ["Database:Name"] = "tests-" + Guid.NewGuid().ToString("N"),
            ["App:PublicBaseUrl"] = "https://localhost",
            ["AntiBot:RateLimits:GlobalPermitPerMinute"] = "10000",
            ["AntiBot:RateLimits:AuthPermitPerMinute"] = "10000",
            ["AntiBot:RateLimits:ApiTokenLimit"] = "10000",
            ["AntiBot:RateLimits:ApiTokensPerPeriod"] = "10000",
            ["AntiBot:Forms:MinSecondsToSubmit"] = "0",
            ["AntiBot:Captcha:Enabled"] = "true",
            ["AntiBot:PwnedPasswords:Enabled"] = "false",
            ["Email:Transport"] = "Log",
            ["Email:FromAddress"] = "no-reply@antibotstarter.test",
            ["Email:Dispatcher:Enabled"] = "false",
        };
        if (overrides is not null)
            foreach (var kv in overrides) _settings[kv.Key] = kv.Value;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(_settings));
        builder.ConfigureTestServices(s =>
        {
            s.AddSingleton<IStartupFilter, TestClientIpStartupFilter>();
            s.AddSingleton<ICaptchaVerifier>(Captcha);
            s.AddSingleton<IDnsResolver>(Dns);
            if (!_realEmail) s.AddSingleton<IEmailSender<IdentityUser>>(Emails);
            else if (_captureTransport) s.AddSingleton<IEmailTransport>(Transport);
        });
    }

    /// <summary>Runs one dispatcher pass (the background loop is disabled in tests).</summary>
    public Task<int> DispatchEmailsAsync() => Services.GetRequiredService<EmailDispatcher>().RunOnceAsync();

    public async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public HttpClient CreateBrowserClient(string ip = "198.51.100.10", string? userAgent = BrowserUa)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
        if (userAgent is not null) client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", userAgent);
        client.DefaultRequestHeaders.Add(TestClientIpStartupFilter.Header, ip);
        return client;
    }
}

public sealed class TestClientIpStartupFilter : IStartupFilter
{
    public const string Header = "X-Test-Client-IP";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((ctx, nxt) =>
        {
            var value = ctx.Request.Headers[Header].ToString();
            ctx.Connection.RemoteIpAddress = IPAddress.Parse(string.IsNullOrEmpty(value) ? "198.51.100.10" : value);
            return nxt(ctx);
        });
        next(app);
    };
}

public sealed class FakeCaptchaVerifier : ICaptchaVerifier
{
    public const string Pass = "captcha-ok";
    private int _calls;
    public int Calls => Volatile.Read(ref _calls);

    public Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken ct)
    {
        Interlocked.Increment(ref _calls);
        return Task.FromResult(token == Pass);
    }
}

public sealed class FakeDnsResolver : IDnsResolver
{
    public ConcurrentDictionary<string, string> Reverse { get; } = new();
    public ConcurrentDictionary<string, IPAddress[]> Forward { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task<string?> ReverseLookupAsync(IPAddress ip, CancellationToken ct) =>
        Task.FromResult(Reverse.TryGetValue(ip.ToString(), out var h) ? h : null);

    public Task<IPAddress[]> ForwardLookupAsync(string host, CancellationToken ct) =>
        Task.FromResult(Forward.TryGetValue(host, out var a) ? a : []);
}

public sealed class CapturingEmailSender : IEmailSender<IdentityUser>
{
    public ConcurrentQueue<(string Email, string Link)> Confirmations { get; } = new();
    public ConcurrentQueue<(string Email, string Link)> Resets { get; } = new();

    public string LastLinkFor(string email) => Confirmations.Last(c => c.Email == email).Link;
    public int CountFor(string email) => Confirmations.Count(c => c.Email == email);
    public string LastResetLinkFor(string email) => Resets.Last(c => c.Email == email).Link;
    public int ResetCountFor(string email) => Resets.Count(c => c.Email == email);

    public Task SendConfirmationLinkAsync(IdentityUser user, string email, string confirmationLink)
    {
        Confirmations.Enqueue((email, confirmationLink));
        return Task.CompletedTask;
    }

    public Task SendPasswordResetLinkAsync(IdentityUser user, string email, string resetLink)
    {
        Resets.Enqueue((email, resetLink));
        return Task.CompletedTask;
    }

    public Task SendPasswordResetCodeAsync(IdentityUser user, string email, string resetCode) => Task.CompletedTask;
}

/// <summary>Records what the dispatcher sends; can be told to fail.</summary>
public sealed class CapturingTransport : IEmailTransport
{
    public ConcurrentQueue<OutgoingEmail> Sent { get; } = new();
    public int FailuresToSimulate { get; set; }

    public Task SendAsync(OutgoingEmail message, CancellationToken ct)
    {
        if (FailuresToSimulate > 0)
        {
            FailuresToSimulate--;
            throw new InvalidOperationException("simulated provider outage");
        }
        Sent.Enqueue(message);
        return Task.CompletedTask;
    }
}

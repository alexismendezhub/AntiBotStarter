using System.Diagnostics.Metrics;

namespace AntiBotStarter.Application.Email;

/// <summary>Metrics of the "AntiBotStarter.Email" meter. Alert on throttled{reason=global_*} and on failed.</summary>
public sealed class EmailMetrics
{
    public const string MeterName = "AntiBotStarter.Email";

    private readonly Counter<long> _queued;
    private readonly Counter<long> _throttled;
    private readonly Counter<long> _sent;
    private readonly Counter<long> _failed;

    public EmailMetrics(IMeterFactory meters)
    {
        var meter = meters.Create(MeterName);
        _queued = meter.CreateCounter<long>("antibot.email.queued");
        _throttled = meter.CreateCounter<long>("antibot.email.throttled", description: "Messages not queued because of an abuse limit");
        _sent = meter.CreateCounter<long>("antibot.email.sent");
        _failed = meter.CreateCounter<long>("antibot.email.failed", description: "Messages given up after MaxAttempts");
    }

    public void Queued(string kind) => _queued.Add(1, new KeyValuePair<string, object?>("kind", kind));
    public void Throttled(string kind, string reason) => _throttled.Add(1, new("kind", kind), new("reason", reason));
    public void Sent(string kind) => _sent.Add(1, new KeyValuePair<string, object?>("kind", kind));
    public void Failed(string kind) => _failed.Add(1, new KeyValuePair<string, object?>("kind", kind));
}

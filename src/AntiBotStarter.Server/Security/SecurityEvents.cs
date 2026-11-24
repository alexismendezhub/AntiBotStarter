using System.Diagnostics.Metrics;

namespace AntiBotStarter.Server.Security;

/// <summary>
/// Central log of rejections: structured log (EventId 9000) + "antibot.rejections" metric
/// tagged with "reason" (exportable to OpenTelemetry / Application Insights).
/// </summary>
public sealed class SecurityEvents
{
    public const string MeterName = "AntiBotStarter.Security";
    private static readonly EventId RejectionEvent = new(9000, "AntiBotRejection");

    private readonly ILogger<SecurityEvents> _logger;
    private readonly Counter<long> _rejections;

    public SecurityEvents(ILogger<SecurityEvents> logger, IMeterFactory meters)
    {
        _logger = logger;
        var meter = meters.Create(MeterName);
        _rejections = meter.CreateCounter<long>("antibot.rejections", description: "Requests rejected by the anti-bot defenses");
    }

    public void Record(string reason, HttpContext ctx, string? detail = null)
    {
        _rejections.Add(1, new KeyValuePair<string, object?>("reason", reason));
        var ua = ctx.Request.Headers.UserAgent.ToString();
        if (ua.Length > 200) ua = ua[..200];
        _logger.LogWarning(RejectionEvent,
            "AntiBot {Reason} ip={ClientIp} method={Method} path={Path} ua={UserAgent} detail={Detail}",
            reason, ClientIp.Key(ctx), ctx.Request.Method, ctx.Request.Path.Value, ua, detail);
    }
}

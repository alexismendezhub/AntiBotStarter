using System.Security.Cryptography;
using AntiBotStarter.Application.Configuration;
using AntiBotStarter.Application.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace AntiBotStarter.Application.Forms;

public enum FormTokenResult { Valid, Missing, Invalid, WrongPurpose, TooFast, Expired, Replayed }

public static class FormPurposes
{
    public const string Login = "login";
    public const string Register = "register";
    public const string ResendConfirmation = "resend";
    public const string ForgotPassword = "forgot";
    public const string ResetPassword = "reset";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Login, Register, ResendConfirmation, ForgotPassword, ResetPassword
    };
}

/// <summary>
/// Form token signed with Data Protection: purpose + issue time + nonce.
///  - Too fast (under MinSecondsToSubmit) → a bot submitting instantly.
///  - Expired (over MaxMinutesValid) → a recycled token.
///  - Reused → a bot replaying the same request (every token is single-use).
/// It cannot be forged without the server's Data Protection keys. Nonces are tracked by IReplayGuard
/// (in memory, or in Redis when several instances run).
/// </summary>
public sealed class FormTokenService
{
    private readonly IDataProtector _protector;
    private readonly TimeProvider _time;
    private readonly IOptionsMonitor<AntiBotOptions> _options;
    private readonly IReplayGuard _replay;

    public FormTokenService(IDataProtectionProvider provider, TimeProvider time, IOptionsMonitor<AntiBotOptions> options, IReplayGuard replay)
    {
        _protector = provider.CreateProtector("AntiBotStarter.FormToken.v1");
        _time = time;
        _options = options;
        _replay = replay;
    }

    public string Issue(string purpose)
    {
        var issued = _time.GetUtcNow().ToUnixTimeMilliseconds();
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        return _protector.Protect($"{purpose}|{issued}|{nonce}");
    }

    public async Task<FormTokenResult> ValidateAndConsumeAsync(string? token, string purpose)
    {
        if (string.IsNullOrWhiteSpace(token)) return FormTokenResult.Missing;
        if (token.Length > 1024) return FormTokenResult.Invalid;

        string payload;
        try { payload = _protector.Unprotect(token); }
        catch (CryptographicException) { return FormTokenResult.Invalid; }

        var parts = payload.Split('|');
        if (parts.Length != 3 || !long.TryParse(parts[1], out var issuedMs)) return FormTokenResult.Invalid;
        if (!string.Equals(parts[0], purpose, StringComparison.Ordinal)) return FormTokenResult.WrongPurpose;

        var s = _options.CurrentValue.Forms;
        var age = _time.GetUtcNow() - DateTimeOffset.FromUnixTimeMilliseconds(issuedMs);
        if (age < TimeSpan.FromSeconds(s.MinSecondsToSubmit)) return FormTokenResult.TooFast;
        if (age > TimeSpan.FromMinutes(s.MaxMinutesValid)) return FormTokenResult.Expired;

        return await _replay.TryConsumeAsync(parts[2], TimeSpan.FromMinutes(s.MaxMinutesValid + 1))
            ? FormTokenResult.Valid
            : FormTokenResult.Replayed;
    }
}

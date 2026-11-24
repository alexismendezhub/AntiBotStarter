using AntiBotStarter.Application.Configuration;
using AntiBotStarter.Application.Forms;
using AntiBotStarter.Infrastructure.Security;
using AntiBotStarter.Application.UnitTests.Support;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Time.Testing;

namespace AntiBotStarter.Application.UnitTests;

public sealed class FormTokenServiceTests : IDisposable
{
    private readonly MemoryReplayGuard _replay = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero));
    private readonly FormTokenService _svc;

    public FormTokenServiceTests()
    {
        var options = new AntiBotOptions { Forms = new FormProtectionSettings { MinSecondsToSubmit = 3, MaxMinutesValid = 30 } };
        _svc = new FormTokenService(new EphemeralDataProtectionProvider(), _time, new StaticOptionsMonitor<AntiBotOptions>(options), _replay);
    }

    public void Dispose() => _replay.Dispose();

    [Fact]
    public async Task ValidAfterMinimumTime()
    {
        var token = _svc.Issue(FormPurposes.Login);
        _time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(FormTokenResult.Valid, await _svc.ValidateAndConsumeAsync(token, FormPurposes.Login));
    }

    [Fact]
    public async Task TooFast()
    {
        var token = _svc.Issue(FormPurposes.Login);
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(FormTokenResult.TooFast, await _svc.ValidateAndConsumeAsync(token, FormPurposes.Login));
    }

    [Fact]
    public async Task TooFast_DoesNotBurnToken()
    {
        var token = _svc.Issue(FormPurposes.Login);
        Assert.Equal(FormTokenResult.TooFast, await _svc.ValidateAndConsumeAsync(token, FormPurposes.Login));
        _time.Advance(TimeSpan.FromSeconds(4));
        Assert.Equal(FormTokenResult.Valid, await _svc.ValidateAndConsumeAsync(token, FormPurposes.Login));
    }

    [Fact]
    public async Task Expired()
    {
        var token = _svc.Issue(FormPurposes.Login);
        _time.Advance(TimeSpan.FromMinutes(31));
        Assert.Equal(FormTokenResult.Expired, await _svc.ValidateAndConsumeAsync(token, FormPurposes.Login));
    }

    [Fact]
    public async Task Replayed()
    {
        var token = _svc.Issue(FormPurposes.Register);
        _time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(FormTokenResult.Valid, await _svc.ValidateAndConsumeAsync(token, FormPurposes.Register));
        Assert.Equal(FormTokenResult.Replayed, await _svc.ValidateAndConsumeAsync(token, FormPurposes.Register));
    }

    [Fact]
    public async Task WrongPurpose()
    {
        var token = _svc.Issue(FormPurposes.Login);
        _time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(FormTokenResult.WrongPurpose, await _svc.ValidateAndConsumeAsync(token, FormPurposes.Register));
    }

    [Theory]
    [InlineData(null, FormTokenResult.Missing)]
    [InlineData("", FormTokenResult.Missing)]
    [InlineData("not-a-token", FormTokenResult.Invalid)]
    public async Task MissingOrInvalid(string? token, FormTokenResult expected) =>
        Assert.Equal(expected, await _svc.ValidateAndConsumeAsync(token, FormPurposes.Login));

    [Fact]
    public async Task Tampered()
    {
        var token = _svc.Issue(FormPurposes.Login);
        var tampered = token[..^4] + (token[^4] == 'A' ? "B" : "A") + token[^3..];
        Assert.Equal(FormTokenResult.Invalid, await _svc.ValidateAndConsumeAsync(tampered, FormPurposes.Login));
    }

    [Fact]
    public async Task TokenFromOtherKeyRing_IsInvalid()
    {
        using var otherReplay = new MemoryReplayGuard();
        var other = new FormTokenService(new EphemeralDataProtectionProvider(), _time,
            new StaticOptionsMonitor<AntiBotOptions>(new AntiBotOptions()), otherReplay);
        var foreign = other.Issue(FormPurposes.Login);
        _time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(FormTokenResult.Invalid, await _svc.ValidateAndConsumeAsync(foreign, FormPurposes.Login));
    }
}

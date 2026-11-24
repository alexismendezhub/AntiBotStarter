using System.Net;
using AntiBotStarter.Application.Configuration;
using AntiBotStarter.Infrastructure.Forms;
using AntiBotStarter.Infrastructure.UnitTests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace AntiBotStarter.Infrastructure.UnitTests;

public sealed class TurnstileCaptchaVerifierTests
{
    private static TurnstileCaptchaVerifier Create(StubHandler handler, params string[] hostnames) =>
        new(new StubHttpClientFactory(handler),
            new StaticOptionsMonitor<AntiBotOptions>(new AntiBotOptions
            {
                Captcha = new CaptchaSettings { SecretKey = "secret", ExpectedHostnames = hostnames }
            }),
            NullLogger<TurnstileCaptchaVerifier>.Instance);

    [Fact]
    public async Task Success_ReturnsTrue_AndSendsSecretTokenAndIp()
    {
        var handler = StubHandler.Json("""{"success":true,"hostname":"www.example.com","error-codes":[]}""");
        Assert.True(await Create(handler).VerifyAsync("tok", "203.0.113.1", CancellationToken.None));

        var (request, body) = handler.Requests.Single();
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://challenges.cloudflare.com/turnstile/v0/siteverify", request.RequestUri!.ToString());
        Assert.Contains("secret=secret", body);
        Assert.Contains("response=tok", body);
        Assert.Contains("remoteip=203.0.113.1", body);
    }

    [Fact]
    public async Task Failure_ReturnsFalse()
    {
        var handler = StubHandler.Json("""{"success":false,"error-codes":["invalid-input-response"]}""");
        Assert.False(await Create(handler).VerifyAsync("tok", null, CancellationToken.None));
    }

    [Fact]
    public async Task UnexpectedHostname_ReturnsFalse()
    {
        var handler = StubHandler.Json("""{"success":true,"hostname":"attacker-site.example"}""");
        Assert.False(await Create(handler, "www.example.com").VerifyAsync("tok", null, CancellationToken.None));
    }

    [Fact]
    public async Task NetworkError_FailsClosed()
    {
        Assert.False(await Create(StubHandler.Throws()).VerifyAsync("tok", null, CancellationToken.None));
    }

    [Fact]
    public async Task ServerError_FailsClosed()
    {
        Assert.False(await Create(StubHandler.Json("{}", HttpStatusCode.InternalServerError)).VerifyAsync("tok", null, CancellationToken.None));
    }

    [Fact]
    public async Task MalformedJson_FailsClosed()
    {
        Assert.False(await Create(StubHandler.Json("<html>")).VerifyAsync("tok", null, CancellationToken.None));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task EmptyToken_ReturnsFalse_WithoutCallingCloudflare(string? token)
    {
        var handler = StubHandler.Json("""{"success":true}""");
        Assert.False(await Create(handler).VerifyAsync(token, null, CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task OversizedToken_ReturnsFalse_WithoutCallingCloudflare()
    {
        var handler = StubHandler.Json("""{"success":true}""");
        Assert.False(await Create(handler).VerifyAsync(new string('a', 2049), null, CancellationToken.None));
        Assert.Empty(handler.Requests);
    }
}

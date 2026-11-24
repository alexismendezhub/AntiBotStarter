using System.Net;
using Microsoft.Extensions.Options;

namespace AntiBotStarter.Infrastructure.UnitTests.Support;

public sealed class StaticOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;
    public T Get(string? name) => value;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

public sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
{
    public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request, body));
        return await responder(request);
    }

    public static StubHandler Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(_ => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") }));

    public static StubHandler Text(string text) =>
        new(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text) }));

    public static StubHandler Throws() => new(_ => throw new HttpRequestException("no network"));
}

public sealed class StubHttpClientFactory(HttpMessageHandler handler, Uri? baseAddress = null) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = baseAddress };
}

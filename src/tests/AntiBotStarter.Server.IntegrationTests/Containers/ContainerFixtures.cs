using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using Testcontainers.Redis;

namespace AntiBotStarter.Server.IntegrationTests.Containers;

/// <summary>
/// Real services in Docker (Testcontainers). When Docker is not available the tests are SKIPPED with the reason,
/// unless ABS_REQUIRE_CONTAINERS=1 (CI and scripts\build-and-test.cmd), where they FAIL instead.
/// </summary>
public abstract class ContainerFixture<TContainer> : IAsyncLifetime where TContainer : class, IContainer
{
    public TContainer? Container { get; private set; }
    public string? Error { get; private set; }

    protected abstract TContainer Build();

    public async Task InitializeAsync()
    {
        try
        {
            Container = Build();
            await Container.StartAsync();
        }
        catch (Exception ex)
        {
            Error = $"{ex.GetType().Name}: {ex.Message}";
        }
    }

    public async Task DisposeAsync()
    {
        if (Container is not null) await Container.DisposeAsync();
    }

    public void EnsureAvailable()
    {
        if (Error is null) return;
        if (Environment.GetEnvironmentVariable("ABS_REQUIRE_CONTAINERS") == "1")
            Assert.Fail("Docker is required (ABS_REQUIRE_CONTAINERS=1) but the container did not start: " + Error);
        Skip.If(true, "Docker not available: " + Error);
    }
}

public sealed class SqlServerFixture : ContainerFixture<MsSqlContainer>
{
    protected override MsSqlContainer Build() => new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public string ConnectionString(string database) =>
        new SqlConnectionStringBuilder(Container!.GetConnectionString()) { InitialCatalog = database }.ConnectionString;
}

public sealed class RedisFixture : ContainerFixture<RedisContainer>
{
    protected override RedisContainer Build() => new RedisBuilder("redis:8-alpine").Build();

    public string ConnectionString => Container!.GetConnectionString();
}

public sealed class MailpitFixture : ContainerFixture<IContainer>
{
    public const int SmtpPort = 1025;
    public const int ApiPort = 8025;

    protected override IContainer Build() => new ContainerBuilder("axllent/mailpit:latest")
        .WithPortBinding(SmtpPort, true)
        .WithPortBinding(ApiPort, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(ApiPort).ForPath("/livez")))
        .Build();

    public string Host => Container!.Hostname;
    public int MappedSmtpPort => Container!.GetMappedPublicPort(SmtpPort);
    public Uri ApiBase => new($"http://{Container!.Hostname}:{Container.GetMappedPublicPort(ApiPort)}/");
}

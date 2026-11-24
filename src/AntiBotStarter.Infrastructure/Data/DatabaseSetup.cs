using AntiBotStarter.Infrastructure.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AntiBotStarter.Infrastructure.Data;

public static class DatabaseSetup
{
    public static IServiceCollection AddAppDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DatabaseOptions>().Bind(configuration.GetSection(DatabaseOptions.Section));

        services.AddDbContext<AppDbContext>((sp, o) =>
        {
            var db = sp.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            if (string.Equals(db.Provider, "InMemory", StringComparison.OrdinalIgnoreCase))
            {
                o.UseInMemoryDatabase(db.Name);
                return;
            }

            var cs = sp.GetRequiredService<IConfiguration>().GetConnectionString("Default");
            if (string.IsNullOrWhiteSpace(cs))
                throw new InvalidOperationException("ConnectionStrings:Default is required for the SqlServer provider.");

            o.UseSqlServer(cs, sql =>
            {
                // Transient faults are common in Azure SQL (failovers, throttling).
                sql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null);
                sql.CommandTimeout(30);
            });
        });

        services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database", tags: ["ready"]);
        return services;
    }

    /// <summary>InMemory → EnsureCreated (tests). SqlServer → migrations only when MigrateOnStartup is on (development).</summary>
    public static async Task PrepareDatabaseAsync(this WebApplication app)
    {
        var db = app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        await using var scope = app.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (string.Equals(db.Provider, "InMemory", StringComparison.OrdinalIgnoreCase))
        {
            await ctx.Database.EnsureCreatedAsync();
            return;
        }

        if (db.MigrateOnStartup)
            await ctx.Database.MigrateAsync();
    }
}

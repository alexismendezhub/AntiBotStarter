using AntiBotStarter.Application;
using AntiBotStarter.Application.Configuration;
using AntiBotStarter.Application.Email;
using AntiBotStarter.Infrastructure;
using AntiBotStarter.Infrastructure.Data;
using AntiBotStarter.Server;
using AntiBotStarter.Server.Endpoints;
using AntiBotStarter.Server.Security;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(AntiBotServiceCollectionExtensions.HardenKestrel);

builder.Services.AddApplication(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddAntiBotProtection();
builder.Services.AddProblemDetails();

// Application Insights / Azure Monitor (OpenTelemetry) only when its connection string is configured.
if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddOpenTelemetry()
        .UseAzureMonitor()
        .WithMetrics(m => m.AddMeter(SecurityEvents.MeterName, EmailMetrics.MeterName));
}

var app = builder.Build();

StartupValidation.Validate(app);
await app.PrepareDatabaseAsync();

// 1. Real client IP (before anything that depends on the IP or the https scheme)
app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler();
    app.UseHsts();
}

// 2. Edge defenses: headers, origin lock, anti-crawler trap + blocklist, User-Agent filter
app.UseAntiBotEdge();

app.UseHttpsRedirection();

// 3. Angular files ("Angular and ASP.NET Core" template)
app.UseDefaultFiles();
app.UseRouting();
app.UseCors(ProxySetup.CorsPolicy);
app.UseAuthentication();
// 4. Rate limiting after authentication so it can limit per user
app.UseRateLimiter();
// Static files behind the global rate limiter (the template uses MapStaticAssets; UseStaticFiles needs no manifest)
app.UseStaticFiles();
app.UseAuthorization();

var antiBot = app.Services.GetRequiredService<IOptions<AntiBotOptions>>().Value;
app.MapAntiBotEndpoints();
app.MapAuthEndpoints();
app.MapMfaEndpoints();
app.MapProductEndpoints(antiBot);

// Readiness for load balancers / App Service health check: database + Redis. Status only, no details.
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") })
    .DisableRateLimiting();

app.MapFallback("/api/{**rest}", () => Results.NotFound());
app.MapFallbackToFile("/index.html");

await app.RunAsync();

public partial class Program;

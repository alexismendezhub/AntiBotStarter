using AntiBotStarter.Application.Configuration;
using AntiBotStarter.Application.Forms;
using AntiBotStarter.Application.Security;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AntiBotStarter.Application;

public static class DependencyInjection
{
    /// <summary>Anti-bot options and the application services (form tokens, search engine verification).</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AntiBotOptions>().Bind(configuration.GetSection(AntiBotOptions.Section));
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<SearchEngineVerifier>();
        services.AddSingleton<FormTokenService>();
        return services;
    }
}

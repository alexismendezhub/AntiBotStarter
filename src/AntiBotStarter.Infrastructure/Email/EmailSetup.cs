using AntiBotStarter.Application.Email;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace AntiBotStarter.Infrastructure.Email;

public static class EmailSetup
{
    public static IServiceCollection AddAppEmail(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<EmailOptions>().Bind(configuration.GetSection(EmailOptions.Section));

        services.AddSingleton<EmailMetrics>();
        services.AddSingleton<DisposableDomains>();
        services.AddScoped<IEmailOutbox, EmailOutbox>();
        services.AddScoped<IEmailSender<IdentityUser>, IdentityEmailSender>();

        services.AddSingleton<LogEmailTransport>();
        services.AddSingleton<SmtpEmailTransport>();
        services.AddSingleton<AcsEmailTransport>();
        services.AddSingleton<IEmailTransport>(sp => sp.GetRequiredService<IOptions<EmailOptions>>().Value.Transport switch
        {
            "Smtp" => sp.GetRequiredService<SmtpEmailTransport>(),
            "AzureCommunicationServices" => sp.GetRequiredService<AcsEmailTransport>(),
            "Log" => sp.GetRequiredService<LogEmailTransport>(),
            var other => throw new InvalidOperationException($"Unknown Email:Transport '{other}'. Use Log, Smtp or AzureCommunicationServices.")
        });

        services.AddSingleton<EmailDispatcher>();
        services.AddHostedService(sp => sp.GetRequiredService<EmailDispatcher>());
        return services;
    }
}

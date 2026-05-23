using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PTDoc.Application.Communication;
using PTDoc.Infrastructure.Communication;
using PTDoc.Infrastructure.Communication.Azure;

namespace PTDoc.Infrastructure.DependencyInjection;

public static class CommunicationServiceCollectionExtensions
{
    public static IServiceCollection AddPTDocCommunication(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var options = LoadOptions(configuration, environment);
        services.AddSingleton(Options.Create(options));
        ValidateConfiguration(options, environment);

        services.AddSingleton<DevelopmentCommunicationMessageStore>();
        services.AddScoped<ICommunicationService, CommunicationService>();
        services.AddScoped<IContactNormalizer, ContactNormalizer>();
        services.AddScoped<IIntakeCommunicationWorkflow, IntakeCommunicationWorkflow>();
        services.AddScoped<IPasswordResetTokenService, PasswordResetTokenService>();
        services.AddScoped<IMessageTemplateRenderer, FileMessageTemplateRenderer>();
        services.AddScoped<ICommunicationAuditWriter, CommunicationAuditWriter>();
        services.AddHostedService<CommunicationRetentionCleanupService>();

        if (IsNullSenderEnvironment(environment))
        {
            services.AddScoped<IEmailSender, NullEmailSender>();
            services.AddScoped<ISmsSender, NullSmsSender>();
        }
        else
        {
            services.AddScoped<IEmailSender, AzureEmailSender>();
            services.AddScoped<ISmsSender, AzureSmsSender>();
        }

        return services;
    }

    private static void ValidateConfiguration(
        CommunicationOptions options,
        IHostEnvironment environment)
    {
        var normalizedPublicBaseUrl = CommunicationText.NormalizePublicBaseUrl(options.PublicBaseUrl);

        if (options.TokenExpiryMinutes.PasswordReset <= 0)
        {
            throw new InvalidOperationException("Communication:TokenExpiryMinutes:PasswordReset must be greater than zero.");
        }

        if (options.TokenExpiryMinutes.Intake <= 0)
        {
            throw new InvalidOperationException("Communication:TokenExpiryMinutes:Intake must be greater than zero.");
        }

        if (options.RateLimits.PasswordResetMaxPerWindow <= 0 ||
            options.RateLimits.PasswordResetWindowMinutes <= 0 ||
            options.RateLimits.IntakeMaxPerDay <= 0)
        {
            throw new InvalidOperationException("Communication:RateLimits values must be greater than zero.");
        }

        if (IsNullSenderEnvironment(environment))
        {
            return;
        }

        if (IsLoopbackUrl(normalizedPublicBaseUrl))
        {
            throw new InvalidOperationException("Communication:PublicBaseUrl must be an explicit non-loopback URL outside Development and Testing.");
        }

        if (string.IsNullOrWhiteSpace(options.RecipientHashSalt))
        {
            throw new InvalidOperationException("Communication:RecipientHashSalt must be configured outside Development and Testing.");
        }

        if (string.IsNullOrWhiteSpace(options.Azure.ConnectionString))
        {
            throw new InvalidOperationException("Communication:Azure:ConnectionString must be configured outside Development and Testing.");
        }

        if (string.IsNullOrWhiteSpace(options.Azure.EmailFromAddress))
        {
            throw new InvalidOperationException("Communication:Azure:EmailFromAddress must be configured outside Development and Testing.");
        }

        if (string.IsNullOrWhiteSpace(options.Azure.SmsFromPhoneNumber))
        {
            throw new InvalidOperationException("Communication:Azure:SmsFromPhoneNumber must be configured outside Development and Testing.");
        }
    }

    private static CommunicationOptions LoadOptions(IConfiguration configuration, IHostEnvironment environment)
    {
        var prefix = CommunicationOptions.SectionName;
        return new CommunicationOptions
        {
            PublicBaseUrl = configuration[$"{prefix}:PublicBaseUrl"] ??
                (IsNullSenderEnvironment(environment) ? "http://localhost:5000" : string.Empty),
            RecipientHashSalt = configuration[$"{prefix}:RecipientHashSalt"] ?? string.Empty,
            TokenExpiryMinutes = new TokenExpiryOptions
            {
                PasswordReset = ReadPositiveInt(configuration, $"{prefix}:TokenExpiryMinutes:PasswordReset", 30),
                Intake = ReadPositiveInt(configuration, $"{prefix}:TokenExpiryMinutes:Intake", 10080)
            },
            Retention = new CommunicationRetentionOptions
            {
                ResetTokensDays = ReadPositiveInt(configuration, $"{prefix}:Retention:ResetTokensDays", 30),
                DeliveryLogsDays = ReadPositiveInt(configuration, $"{prefix}:Retention:DeliveryLogsDays", 2190)
            },
            RateLimits = new CommunicationRateLimitOptions
            {
                PasswordResetMaxPerWindow = ReadPositiveInt(configuration, $"{prefix}:RateLimits:PasswordResetMaxPerWindow", 3),
                PasswordResetWindowMinutes = ReadPositiveInt(configuration, $"{prefix}:RateLimits:PasswordResetWindowMinutes", 15),
                IntakeMaxPerDay = ReadPositiveInt(configuration, $"{prefix}:RateLimits:IntakeMaxPerDay", 5)
            },
            Azure = new AzureCommunicationOptions
            {
                ConnectionString = configuration[$"{prefix}:Azure:ConnectionString"] ?? string.Empty,
                EmailFromAddress = configuration[$"{prefix}:Azure:EmailFromAddress"] ?? string.Empty,
                SmsFromPhoneNumber = configuration[$"{prefix}:Azure:SmsFromPhoneNumber"] ?? string.Empty
            }
        };
    }

    private static int ReadPositiveInt(IConfiguration configuration, string key, int fallback)
        => int.TryParse(configuration[key], out var value) && value > 0 ? value : fallback;

    private static bool IsLoopbackUrl(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.IsLoopback;

    private static bool IsNullSenderEnvironment(IHostEnvironment environment)
        => environment.IsDevelopment() || environment.IsEnvironment("Testing");
}

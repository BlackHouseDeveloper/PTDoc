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
        var options = LoadOptions(configuration);
        services.AddSingleton(Options.Create(options));
        ValidateConfiguration(options, environment);

        services.AddScoped<ICommunicationService, CommunicationService>();
        services.AddScoped<IPasswordResetTokenService, PasswordResetTokenService>();
        services.AddScoped<IMessageTemplateRenderer, FileMessageTemplateRenderer>();
        services.AddScoped<ICommunicationAuditWriter, CommunicationAuditWriter>();

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
        CommunicationText.NormalizePublicBaseUrl(options.PublicBaseUrl);

        if (options.TokenExpiryMinutes.PasswordReset <= 0)
        {
            throw new InvalidOperationException("Communication:TokenExpiryMinutes:PasswordReset must be greater than zero.");
        }

        if (options.TokenExpiryMinutes.Intake <= 0)
        {
            throw new InvalidOperationException("Communication:TokenExpiryMinutes:Intake must be greater than zero.");
        }

        if (IsNullSenderEnvironment(environment))
        {
            return;
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

    private static CommunicationOptions LoadOptions(IConfiguration configuration)
    {
        var prefix = CommunicationOptions.SectionName;
        return new CommunicationOptions
        {
            PublicBaseUrl = configuration[$"{prefix}:PublicBaseUrl"] ?? "http://localhost:5000",
            RecipientHashSalt = configuration[$"{prefix}:RecipientHashSalt"] ?? string.Empty,
            TokenExpiryMinutes = new TokenExpiryOptions
            {
                PasswordReset = ReadPositiveInt(configuration, $"{prefix}:TokenExpiryMinutes:PasswordReset", 30),
                Intake = ReadPositiveInt(configuration, $"{prefix}:TokenExpiryMinutes:Intake", 10080)
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
        => int.TryParse(configuration[key], out var value) ? value : fallback;

    private static bool IsNullSenderEnvironment(IHostEnvironment environment)
        => environment.IsDevelopment() || environment.IsEnvironment("Testing");
}

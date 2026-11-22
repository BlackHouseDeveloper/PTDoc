using Microsoft.Extensions.DependencyInjection;
using PTDoc.Application.Services;

namespace PTDoc.UI.Services;

public delegate IIntakeService StandaloneIntakeClientFactory(IntakeSessionToken session);

public static class IntakeServiceCollectionExtensions
{
    public static IServiceCollection AddIntakeApiClient(this IServiceCollection services)
    {
        services.AddScoped<IIntakeService, IntakeApiService>();
        services.AddScoped<StandaloneIntakeClientFactory>(sp =>
            session => ActivatorUtilities.CreateInstance<IntakeApiService>(sp, session));
        services.AddScoped<IntakeInviteRenewal>();
        return services;
    }
}

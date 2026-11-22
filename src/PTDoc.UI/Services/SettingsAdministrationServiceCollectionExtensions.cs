using Microsoft.Extensions.DependencyInjection;
using PTDoc.Application.Settings;

namespace PTDoc.UI.Services;

public static class SettingsAdministrationServiceCollectionExtensions
{
    public static IServiceCollection AddSettingsAdministrationApiClient(this IServiceCollection services)
    {
        services.AddAppointmentAndKioskApiClient();
        services.AddScoped<IRolePermissionAdministrationService>(sp => sp.GetRequiredService<SettingsAdministrationApiClient>());
        services.AddScoped<ISecurityPolicyAdministrationService>(sp => sp.GetRequiredService<SettingsAdministrationApiClient>());
        services.AddScoped<IAutoCheckInAdministrationService>(sp => sp.GetRequiredService<SettingsAdministrationApiClient>());
        return services;
    }

    public static IServiceCollection AddAppointmentAndKioskApiClient(this IServiceCollection services)
    {
        services.AddScoped<SettingsAdministrationApiClient>();
        services.AddScoped<ISchedulingAdministrationService>(sp => sp.GetRequiredService<SettingsAdministrationApiClient>());
        services.AddScoped<IKioskCheckInService>(sp => sp.GetRequiredService<SettingsAdministrationApiClient>());
        return services;
    }
}

using PTDoc.Application.Auth;
using PTDoc.Application.Services;

namespace PTDoc.Infrastructure.Services;

/// <summary>
/// Implementation of role-based permissions service
/// </summary>
public class RoleService : IRoleService
{
    public bool HasRole(UserInfo user, string role)
    {
        return user.Roles.Contains(role, StringComparer.OrdinalIgnoreCase);
    }

    public DashboardLayoutConfig GetDashboardLayout(UserInfo user)
    {
        // Admin and owner get full access
        if (HasRole(user, Roles.Admin) || HasRole(user, Roles.Owner))
        {
            return new DashboardLayoutConfig
            {
                ShowClinicalWidgets = true,
                ShowBillingWidgets = true,
                ShowScheduleWidgets = true,
                ShowSystemHealth = true,
                WelcomeMessage = HasRole(user, Roles.Owner) ? "Owner Dashboard" : "Admin Dashboard"
            };
        }

        // PT gets standard clinical view
        if (HasRole(user, Roles.PT))
        {
            return new DashboardLayoutConfig
            {
                ShowClinicalWidgets = true,
                ShowBillingWidgets = true,
                ShowScheduleWidgets = true,
                ShowSystemHealth = false,
                WelcomeMessage = "Welcome back"
            };
        }

        // PTA gets clinical view with focus on assigned patients
        if (HasRole(user, Roles.PTA))
        {
            return new DashboardLayoutConfig
            {
                ShowClinicalWidgets = true,
                ShowBillingWidgets = false,
                ShowScheduleWidgets = true,
                ShowSystemHealth = false,
                WelcomeMessage = "Welcome back"
            };
        }

        // Front Desk focuses on schedule
        if (HasRole(user, Roles.FrontDesk))
        {
            return new DashboardLayoutConfig
            {
                ShowClinicalWidgets = false,
                ShowBillingWidgets = false,
                ShowScheduleWidgets = true,
                ShowSystemHealth = false,
                WelcomeMessage = "Front Desk Dashboard"
            };
        }

        // Billing focuses on authorizations
        if (HasRole(user, Roles.Billing))
        {
            return new DashboardLayoutConfig
            {
                ShowClinicalWidgets = false,
                ShowBillingWidgets = true,
                ShowScheduleWidgets = false,
                ShowSystemHealth = false,
                WelcomeMessage = "Billing Dashboard"
            };
        }

        // Default: show everything
        return new DashboardLayoutConfig();
    }
}

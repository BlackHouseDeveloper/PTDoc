using PTDoc.Application.Auth;

namespace PTDoc.Application.Services;

/// <summary>
/// Service for determining user roles and permissions
/// </summary>
public interface IRoleService
{
    /// <summary>
    /// Check if user has a specific role
    /// </summary>
    bool HasRole(UserInfo user, string role);
    
    /// <summary>
    /// Get dashboard layout configuration for user's role
    /// </summary>
    DashboardLayoutConfig GetDashboardLayout(UserInfo user);
}

/// <summary>
/// Dashboard layout configuration based on role
/// </summary>
public sealed record DashboardLayoutConfig
{
    public bool ShowClinicalWidgets { get; init; } = true;
    public bool ShowBillingWidgets { get; init; } = true;
    public bool ShowScheduleWidgets { get; init; } = true;
    public bool ShowSystemHealth { get; init; } = false;
    public string WelcomeMessage { get; init; } = "Welcome back";
}

/// <summary>
/// Common role names
/// </summary>
public static class Roles
{
    public const string PT = "PT";
    public const string PTA = "PTA";
    public const string FrontDesk = "FrontDesk";
    public const string Billing = "Billing";
    public const string Admin = "Admin";
}

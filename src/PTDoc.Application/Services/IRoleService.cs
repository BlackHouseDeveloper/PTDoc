using Microsoft.AspNetCore.Authorization;
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
    public const string Aide = "Aide";
    public const string Patient = "Patient";
}

/// <summary>
/// Named authorization policy identifiers used across API endpoints.
/// Use <see cref="AddPTDocAuthorizationPolicies"/> to register all policies.
/// </summary>
public static class AuthorizationPolicies
{
    /// <summary>Read patient demographics — PT, PTA, Admin, Aide.</summary>
    public const string PatientRead = "PatientRead";

    /// <summary>Create/update patient records — PT, PTA, Admin.</summary>
    public const string PatientWrite = "PatientWrite";

    /// <summary>Read clinical notes — PT, PTA, Admin.</summary>
    public const string NoteRead = "NoteRead";

    /// <summary>Create/update draft notes — PT, PTA.</summary>
    public const string NoteWrite = "NoteWrite";

    /// <summary>Read or submit intake forms — PT, PTA, Admin, Patient.</summary>
    public const string IntakeRead = "IntakeRead";

    /// <summary>Create intake forms for patients — PT, PTA, Admin.</summary>
    public const string IntakeWrite = "IntakeWrite";

    /// <summary>Access sync and compliance evaluation endpoints — PT, PTA, Admin.</summary>
    public const string ClinicalStaff = "ClinicalStaff";

    /// <summary>
    /// Registers all PTDoc RBAC policies on <paramref name="options"/>.
    /// Call this from both <c>PTDoc.Api/Program.cs</c> and authorization tests to ensure
    /// a single authoritative policy definition shared by production and test code.
    /// </summary>
    public static void AddPTDocAuthorizationPolicies(this AuthorizationOptions options)
    {
        // PatientRead: clinical staff and therapy aides can view patient demographics
        options.AddPolicy(PatientRead,
            p => p.RequireRole(Roles.PT, Roles.PTA, Roles.Admin, Roles.Aide));

        // PatientWrite: licensed clinicians and admin can create/update patient records
        options.AddPolicy(PatientWrite,
            p => p.RequireRole(Roles.PT, Roles.PTA, Roles.Admin));

        // NoteRead: only clinical staff can read clinical notes (not Aide or Patient)
        options.AddPolicy(NoteRead,
            p => p.RequireRole(Roles.PT, Roles.PTA, Roles.Admin));

        // NoteWrite: only licensed clinicians can create/update notes (Admin is read-only per FSD §3.1)
        options.AddPolicy(NoteWrite,
            p => p.RequireRole(Roles.PT, Roles.PTA));

        // IntakeRead: clinical staff and patients can read intake forms
        options.AddPolicy(IntakeRead,
            p => p.RequireRole(Roles.PT, Roles.PTA, Roles.Admin, Roles.Patient));

        // IntakeWrite: clinical staff can create intake forms for patients
        options.AddPolicy(IntakeWrite,
            p => p.RequireRole(Roles.PT, Roles.PTA, Roles.Admin));

        // ClinicalStaff: sync and compliance evaluation — all authenticated staff
        options.AddPolicy(ClinicalStaff,
            p => p.RequireRole(Roles.PT, Roles.PTA, Roles.Admin));
    }
}

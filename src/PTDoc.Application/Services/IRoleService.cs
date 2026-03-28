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
    public const string Owner = "Owner";
    public const string PT = "PT";
    public const string PTA = "PTA";
    public const string FrontDesk = "FrontDesk";
    public const string Billing = "Billing";
    public const string Admin = "Admin";
    public const string Aide = "Aide";
    public const string Patient = "Patient";
    /// <summary>Practice Manager: user roles, scheduling templates, fee schedules, reporting, NO clinical editing.</summary>
    public const string PracticeManager = "PracticeManager";
}

/// <summary>
/// Named authorization policy identifiers used across API endpoints.
/// Use <see cref="AddPTDocAuthorizationPolicies"/> to register all policies.
/// </summary>
public static class AuthorizationPolicies
{
    /// <summary>Patient-only access to the Wibbi launch broker.</summary>
    public const string PatientHepAccess = "PatientHepAccess";

    /// <summary>Read patient demographics — PT, PTA, Admin, Owner, Aide, FrontDesk, Billing.</summary>
    public const string PatientRead = "PatientRead";

    /// <summary>Create/update patient records — PT, PTA, Admin (Owner is read-only per Role→Capability matrix).</summary>
    public const string PatientWrite = "PatientWrite";

    /// <summary>Read clinical notes — PT, PTA, Admin, Owner, Billing (view-only per FSD §3.2).</summary>
    public const string NoteRead = "NoteRead";

    /// <summary>Create/update draft notes — PT, PTA only (no Admin, Owner, Billing, FrontDesk, Aide, Patient).</summary>
    public const string NoteWrite = "NoteWrite";

    /// <summary>Read or submit intake forms — PT, PTA, Admin, Owner, Patient, FrontDesk.</summary>
    public const string IntakeRead = "IntakeRead";

    /// <summary>Create/manage intake forms — PT, PTA, Admin, FrontDesk (Owner is read-only per Role→Capability matrix).</summary>
    public const string IntakeWrite = "IntakeWrite";

    /// <summary>Access sync and compliance evaluation endpoints — PT, PTA, Admin, Owner.</summary>
    public const string ClinicalStaff = "ClinicalStaff";

    /// <summary>Admin-only access — system settings, diagnostics, configuration — Admin, Owner.</summary>
    public const string AdminOnly = "AdminOnly";

    /// <summary>Billing access — charge review, CPT/ICD edits, ERA/EOB — Billing role only.</summary>
    public const string BillingAccess = "BillingAccess";

    /// <summary>Scheduling access — full scheduling management — PT, PTA, FrontDesk, Admin, PracticeManager.</summary>
    public const string SchedulingAccess = "SchedulingAccess";

    /// <summary>Co-sign clinical notes — PT role only (countersigns PTA-authored notes).</summary>
    public const string NoteCoSign = "NoteCoSign";

    /// <summary>Export clinical notes as PDF — PT, PTA, Admin only. Owner and Billing are read-only per role matrix.</summary>
    public const string NoteExport = "NoteExport";

    /// <summary>
    /// Registers all PTDoc RBAC policies on <paramref name="options"/>.
    /// Call this from both <c>PTDoc.Api/Program.cs</c> and authorization tests to ensure
    /// a single authoritative policy definition shared by production and test code.
    /// </summary>
    public static void AddPTDocAuthorizationPolicies(this AuthorizationOptions options)
    {
        // PatientRead: clinical staff, aides, front desk, billing can view patient demographics
        options.AddPolicy(PatientRead,
            p => p.RequireRole(Roles.PT, Roles.PTA, Roles.Admin, Roles.Owner, Roles.Aide,
                               Roles.FrontDesk, Roles.Billing));

        // PatientWrite: licensed clinicians and admin can create/update patient records
        // Owner is read-only per Role→Capability matrix ("Edit patient demographics" = Deny for Owner)
        options.AddPolicy(PatientWrite,
            p => p.RequireRole(Roles.PT, Roles.PTA, Roles.Admin));

        // NoteRead: clinical staff + Billing can read clinical notes (not Aide, FrontDesk, or Patient)
        // Billing is "clinical notes VIEW ONLY" per canonical role matrix
        options.AddPolicy(NoteRead,
            p => p.RequireRole(Roles.PT, Roles.PTA, Roles.Admin, Roles.Owner, Roles.Billing));

        // NoteWrite: only licensed clinicians can create/update notes (Admin is read-only per FSD §3.1)
        // Billing, Owner, FrontDesk, Aide, Patient, PracticeManager CANNOT write notes
        options.AddPolicy(NoteWrite,
            p => p.RequireRole(Roles.PT, Roles.PTA));

        // IntakeRead: clinical staff, front desk, and patients can read intake forms
        options.AddPolicy(IntakeRead,
            p => p.RequireRole(Roles.PT, Roles.PTA, Roles.Admin, Roles.Owner, Roles.Patient, Roles.FrontDesk));

        // IntakeWrite: clinical staff and front desk can create/manage intake forms for patients
        // Owner is read-only per Role→Capability matrix ("Start intake invite" = Deny for Owner)
        options.AddPolicy(IntakeWrite,
            p => p.RequireRole(Roles.PT, Roles.PTA, Roles.Admin, Roles.FrontDesk));

        // PatientHepAccess: only patient identities can launch their own HEP portal session
        options.AddPolicy(PatientHepAccess,
            p => p.RequireRole(Roles.Patient));

        // ClinicalStaff: sync and compliance evaluation — all authenticated clinical staff
        options.AddPolicy(ClinicalStaff,
            p => p.RequireRole(Roles.PT, Roles.PTA, Roles.Admin, Roles.Owner));

        // AdminOnly: system settings and operational diagnostics — Owner and Admin only
        options.AddPolicy(AdminOnly,
            p => p.RequireRole(Roles.Admin, Roles.Owner));

        // BillingAccess: charge review, CPT/ICD edits, ERA/EOB — Billing, Admin, and Owner roles
        options.AddPolicy(BillingAccess,
            p => p.RequireRole(Roles.Billing, Roles.Admin, Roles.Owner));

        // SchedulingAccess: scheduling management — clinical staff (PT, PTA, Admin, Owner), front desk, practice manager
        options.AddPolicy(SchedulingAccess,
            p => p.RequireRole(Roles.PT, Roles.PTA, Roles.FrontDesk, Roles.Admin,
                               Roles.Owner, Roles.PracticeManager));

        // NoteCoSign: PT-only endpoint for countersigning PTA-authored notes
        options.AddPolicy(NoteCoSign,
            p => p.RequireRole(Roles.PT));

        // NoteExport: PDF export — clinical staff (PT, PTA) + Admin only.
        // Owner and Billing are read-only for clinical notes and may not trigger PDF export.
        options.AddPolicy(NoteExport,
            p => p.RequireRole(Roles.PT, Roles.PTA, Roles.Admin));
    }
}

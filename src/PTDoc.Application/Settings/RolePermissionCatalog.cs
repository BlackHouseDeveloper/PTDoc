using PTDoc.Application.Services;
using PTDoc.Core.Models;

namespace PTDoc.Application.Settings;

public sealed record CapabilityDefinition(
    CapabilityKey Key,
    string Name,
    string Description,
    bool IsSupported);

public sealed record SettingsRoleDefinition(string Key, string DisplayName, bool IsReadOnly);

/// <summary>
/// Canonical restrictive role baseline used for clinic seeding and fail-closed fallback.
/// Domain guards remain authoritative even when a capability level permits an operation.
/// </summary>
public static class RolePermissionCatalog
{
    public static readonly IReadOnlyList<SettingsRoleDefinition> Roles =
    [
        new(RolesConstants.Admin, "Administrator", false),
        new(RolesConstants.Owner, "Owner / Executive", true),
        new(RolesConstants.PracticeManager, "Practice Manager", false),
        new(RolesConstants.PT, "PT / DPT", false),
        new(RolesConstants.PTA, "PTA", false),
        new(RolesConstants.Aide, "Therapy Aide / Rehab Tech", false),
        new(RolesConstants.FrontDesk, "Front Desk / Patient Care Coordinator", false),
        new(RolesConstants.Billing, "Billing Specialist", false),
        new(RolesConstants.Patient, "Patient", true)
    ];

    public static readonly IReadOnlyList<CapabilityDefinition> Capabilities =
    [
        new(CapabilityKey.ClinicalNotesView, "View Clinical Notes", "Read access to SOAP notes and evaluations", true),
        new(CapabilityKey.ClinicalNotesCreate, "Create Clinical Notes", "Write new SOAP notes, evaluations, and progress notes", true),
        new(CapabilityKey.ClinicalNotesEditOwn, "Edit Own Notes", "Modify notes created by self before signing", true),
        new(CapabilityKey.ClinicalNotesEditOthers, "Edit Others' Notes", "Modify notes created by other clinicians", false),
        new(CapabilityKey.ClinicalNotesSign, "Sign/Submit Notes", "Finalize and submit clinical documentation", true),
        new(CapabilityKey.ClinicalNotesCoSignPta, "Co-Sign PTA Notes", "Review and co-sign notes from PTAs", true),
        new(CapabilityKey.ClinicalNotesDelete, "Delete Notes", "Remove clinical documentation with retained audit history", false),
        new(CapabilityKey.ScheduleViewOwn, "View Own Schedule", "See personal appointments and availability", true),
        new(CapabilityKey.ScheduleViewAll, "View All Schedules", "See schedules for all clinicians", true),
        new(CapabilityKey.AppointmentsCreate, "Create Appointments", "Book new patient appointments", true),
        new(CapabilityKey.AppointmentsModify, "Modify Appointments", "Reschedule or cancel appointments", true),
        new(CapabilityKey.ClinicianAvailabilityManage, "Manage Clinician Availability", "Set working hours and time off", true),
        new(CapabilityKey.BillingInformationView, "View Billing Information", "See patient balances and billing codes", true),
        new(CapabilityKey.CptCodesAdd, "Add CPT Codes", "Add billing codes to clinical notes", true),
        new(CapabilityKey.InsuranceClaimsSubmit, "Submit Insurance Claims", "Process and submit claims to payers", false),
        new(CapabilityKey.PayerInformationManage, "Manage Payer Information", "Add or edit payer contracts", false),
        new(CapabilityKey.BillingReportsView, "View Billing Reports", "Access financial reports and revenue analytics", false),
        new(CapabilityKey.StaffMessagesSend, "Internal Staff Messaging", "Send messages to staff members", false),
        new(CapabilityKey.PatientMessagesSend, "Message Patients", "Send compliant messages to patients", true),
        new(CapabilityKey.BroadcastMessagesSend, "Broadcast Messages", "Send announcements to staff or patients", false),
        new(CapabilityKey.ClinicalReportsView, "Clinical Reports", "View patient outcomes and treatment analytics", false),
        new(CapabilityKey.ProductivityReportsView, "Productivity Reports", "View clinician productivity and utilization", false),
        new(CapabilityKey.FinancialReportsView, "Financial Reports", "Access revenue and collection reports", false),
        new(CapabilityKey.ComplianceReportsView, "Compliance Reports", "View audit logs and compliance metrics", true),
        new(CapabilityKey.ReportsExport, "Export Reports", "Download reports as PDF or CSV", true),
        new(CapabilityKey.UsersManage, "Manage Users", "Create, edit, and deactivate user accounts", true),
        new(CapabilityKey.RolesPermissionsManage, "Manage Roles & Permissions", "Edit role permissions and access levels", true),
        new(CapabilityKey.ClinicSettingsManage, "Clinic Settings", "Modify clinic information and preferences", true),
        new(CapabilityKey.DocumentationTemplatesManage, "Manage Templates", "Create and edit documentation templates", false),
        new(CapabilityKey.IntegrationsManage, "Manage Integrations", "Configure third-party integrations", true)
    ];

    public static PermissionLevel GetCanonicalLevel(string roleKey, CapabilityKey capability) =>
        CanonicalLevels.TryGetValue((NormalizeRole(roleKey), capability), out var level)
            ? level
            : PermissionLevel.None;

    public static PermissionLevel GetLockedMinimum(string roleKey, CapabilityKey capability)
    {
        var normalizedRole = NormalizeRole(roleKey);
        if (normalizedRole == RolesConstants.Admin &&
            capability is CapabilityKey.RolesPermissionsManage or CapabilityKey.UsersManage)
        {
            return PermissionLevel.Full;
        }

        return PermissionLevel.None;
    }

    public static SettingsRoleDefinition? FindRole(string roleKey) =>
        Roles.FirstOrDefault(role => string.Equals(role.Key, NormalizeRole(roleKey), StringComparison.Ordinal));

    public static CapabilityDefinition FindCapability(CapabilityKey capabilityKey) =>
        Capabilities.Single(capability => capability.Key == capabilityKey);

    public static string NormalizeRole(string roleKey) => roleKey.Trim() switch
    {
        "Owner / Executive Admin" => RolesConstants.Owner,
        "Owner / Executive" => RolesConstants.Owner,
        "Administrator" => RolesConstants.Admin,
        "Practice Manager" => RolesConstants.PracticeManager,
        "PT / DPT" => RolesConstants.PT,
        "Therapy Aide / Rehab Tech" => RolesConstants.Aide,
        "Front Desk / Patient Care Coordinator" => RolesConstants.FrontDesk,
        "Billing Specialist" => RolesConstants.Billing,
        var role => role
    };

    private static readonly IReadOnlyDictionary<(string Role, CapabilityKey Capability), PermissionLevel> CanonicalLevels =
        BuildCanonicalLevels();

    private static IReadOnlyDictionary<(string Role, CapabilityKey Capability), PermissionLevel> BuildCanonicalLevels()
    {
        var levels = new Dictionary<(string, CapabilityKey), PermissionLevel>();

        Set(levels, RolesConstants.Admin, PermissionLevel.View,
            CapabilityKey.ClinicalNotesView, CapabilityKey.BillingInformationView,
            CapabilityKey.ClinicalReportsView, CapabilityKey.ProductivityReportsView,
            CapabilityKey.FinancialReportsView, CapabilityKey.ComplianceReportsView);
        Set(levels, RolesConstants.Admin, PermissionLevel.Full,
            CapabilityKey.ScheduleViewOwn, CapabilityKey.ScheduleViewAll,
            CapabilityKey.AppointmentsCreate, CapabilityKey.AppointmentsModify,
            CapabilityKey.ClinicianAvailabilityManage, CapabilityKey.UsersManage,
            CapabilityKey.RolesPermissionsManage, CapabilityKey.ClinicSettingsManage,
            CapabilityKey.IntegrationsManage, CapabilityKey.ReportsExport);

        Set(levels, RolesConstants.Owner, PermissionLevel.View,
            CapabilityKey.ClinicalNotesView, CapabilityKey.ScheduleViewOwn,
            CapabilityKey.ScheduleViewAll, CapabilityKey.BillingInformationView,
            CapabilityKey.ClinicalReportsView, CapabilityKey.ProductivityReportsView,
            CapabilityKey.FinancialReportsView, CapabilityKey.ComplianceReportsView,
            CapabilityKey.ClinicSettingsManage);

        Set(levels, RolesConstants.PracticeManager, PermissionLevel.View,
            CapabilityKey.ScheduleViewOwn, CapabilityKey.ScheduleViewAll,
            CapabilityKey.BillingInformationView, CapabilityKey.ProductivityReportsView,
            CapabilityKey.FinancialReportsView, CapabilityKey.ComplianceReportsView);
        Set(levels, RolesConstants.PracticeManager, PermissionLevel.Full,
            CapabilityKey.AppointmentsCreate, CapabilityKey.AppointmentsModify,
            CapabilityKey.ClinicianAvailabilityManage, CapabilityKey.UsersManage,
            CapabilityKey.ClinicSettingsManage, CapabilityKey.IntegrationsManage);

        Set(levels, RolesConstants.PT, PermissionLevel.View,
            CapabilityKey.ClinicalNotesView, CapabilityKey.ScheduleViewOwn,
            CapabilityKey.ScheduleViewAll, CapabilityKey.BillingInformationView,
            CapabilityKey.ClinicalReportsView, CapabilityKey.ProductivityReportsView);
        Set(levels, RolesConstants.PT, PermissionLevel.Edit,
            CapabilityKey.ClinicalNotesCreate, CapabilityKey.ClinicalNotesEditOwn,
            CapabilityKey.AppointmentsCreate, CapabilityKey.AppointmentsModify,
            CapabilityKey.CptCodesAdd, CapabilityKey.StaffMessagesSend,
            CapabilityKey.PatientMessagesSend);
        Set(levels, RolesConstants.PT, PermissionLevel.Full,
            CapabilityKey.ClinicalNotesSign, CapabilityKey.ClinicalNotesCoSignPta);

        Set(levels, RolesConstants.PTA, PermissionLevel.View,
            CapabilityKey.ClinicalNotesView, CapabilityKey.ScheduleViewOwn,
            CapabilityKey.ScheduleViewAll, CapabilityKey.BillingInformationView);
        Set(levels, RolesConstants.PTA, PermissionLevel.Edit,
            CapabilityKey.ClinicalNotesCreate, CapabilityKey.ClinicalNotesEditOwn,
            CapabilityKey.AppointmentsCreate, CapabilityKey.AppointmentsModify,
            CapabilityKey.CptCodesAdd, CapabilityKey.StaffMessagesSend,
            CapabilityKey.PatientMessagesSend);
        Set(levels, RolesConstants.PTA, PermissionLevel.Full, CapabilityKey.ClinicalNotesSign);

        Set(levels, RolesConstants.Aide, PermissionLevel.View,
            CapabilityKey.ScheduleViewOwn, CapabilityKey.ScheduleViewAll);

        Set(levels, RolesConstants.FrontDesk, PermissionLevel.View,
            CapabilityKey.ScheduleViewAll, CapabilityKey.BillingInformationView);
        Set(levels, RolesConstants.FrontDesk, PermissionLevel.Edit,
            CapabilityKey.AppointmentsCreate, CapabilityKey.AppointmentsModify,
            CapabilityKey.PatientMessagesSend);

        Set(levels, RolesConstants.Billing, PermissionLevel.View,
            CapabilityKey.ClinicalNotesView, CapabilityKey.BillingInformationView,
            CapabilityKey.BillingReportsView, CapabilityKey.FinancialReportsView);
        Set(levels, RolesConstants.Billing, PermissionLevel.Edit, CapabilityKey.CptCodesAdd);

        return levels;
    }

    private static void Set(
        IDictionary<(string, CapabilityKey), PermissionLevel> levels,
        string role,
        PermissionLevel level,
        params CapabilityKey[] capabilities)
    {
        foreach (var capability in capabilities)
        {
            levels[(role, capability)] = level;
        }
    }

    private static class RolesConstants
    {
        public const string Owner = PTDoc.Application.Services.Roles.Owner;
        public const string Admin = PTDoc.Application.Services.Roles.Admin;
        public const string PracticeManager = PTDoc.Application.Services.Roles.PracticeManager;
        public const string PT = PTDoc.Application.Services.Roles.PT;
        public const string PTA = PTDoc.Application.Services.Roles.PTA;
        public const string Aide = PTDoc.Application.Services.Roles.Aide;
        public const string FrontDesk = PTDoc.Application.Services.Roles.FrontDesk;
        public const string Billing = PTDoc.Application.Services.Roles.Billing;
        public const string Patient = PTDoc.Application.Services.Roles.Patient;
    }
}

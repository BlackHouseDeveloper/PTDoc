namespace PTDoc.Core.Models;

/// <summary>
/// Stable capabilities displayed by the clinic role-permission matrix.
/// Numeric values are explicit so persisted values remain stable when new capabilities are added.
/// </summary>
public enum CapabilityKey
{
    ClinicalNotesView = 1,
    ClinicalNotesCreate = 2,
    ClinicalNotesEditOwn = 3,
    ClinicalNotesEditOthers = 4,
    ClinicalNotesSign = 5,
    ClinicalNotesCoSignPta = 6,
    ClinicalNotesDelete = 7,
    ScheduleViewOwn = 8,
    ScheduleViewAll = 9,
    AppointmentsCreate = 10,
    AppointmentsModify = 11,
    ClinicianAvailabilityManage = 12,
    BillingInformationView = 13,
    CptCodesAdd = 14,
    InsuranceClaimsSubmit = 15,
    PayerInformationManage = 16,
    BillingReportsView = 17,
    StaffMessagesSend = 18,
    PatientMessagesSend = 19,
    BroadcastMessagesSend = 20,
    ClinicalReportsView = 21,
    ProductivityReportsView = 22,
    FinancialReportsView = 23,
    ComplianceReportsView = 24,
    ReportsExport = 25,
    UsersManage = 26,
    RolesPermissionsManage = 27,
    ClinicSettingsManage = 28,
    DocumentationTemplatesManage = 29,
    IntegrationsManage = 30
}

public enum PermissionLevel
{
    None = 0,
    View = 1,
    Edit = 2,
    Full = 3
}

public enum AuthorizationRolloutMode
{
    Static = 0,
    Shadow = 1,
    Enforced = 2
}

public enum MfaEnforcementMode
{
    Off = 0,
    GracePeriod = 1,
    Enforced = 2
}

[Flags]
public enum WeekdayFlags
{
    None = 0,
    Sunday = 1 << 0,
    Monday = 1 << 1,
    Tuesday = 1 << 2,
    Wednesday = 1 << 3,
    Thursday = 1 << 4,
    Friday = 1 << 5,
    Saturday = 1 << 6,
    Weekdays = Monday | Tuesday | Wednesday | Thursday | Friday,
    All = Sunday | Monday | Tuesday | Wednesday | Thursday | Friday | Saturday
}

public enum ReminderChannel
{
    Email = 0,
    Sms = 1
}

public enum ReminderDispatchStatus
{
    Pending = 0,
    Processing = 1,
    Sent = 2,
    RetryScheduled = 3,
    DeadLetter = 4,
    Cancelled = 5,
    Suppressed = 6
}

public enum ReminderDispatchPurpose
{
    AppointmentReminder = 0,
    AutoCheckIn = 1
}

/// <summary>
/// Clinic-scoped dynamic role capability setting.
/// </summary>
public class RoleCapabilityPermission
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClinicId { get; set; }
    public string RoleKey { get; set; } = string.Empty;
    public CapabilityKey CapabilityKey { get; set; }
    public PermissionLevel Level { get; set; }
    public PermissionLevel LockedMinimum { get; set; }
    public long Version { get; set; } = 1;
    public Guid UpdatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public Clinic? Clinic { get; set; }
}

/// <summary>
/// Versioned, clinic-scoped authentication and access policy.
/// Mandatory auditing is intentionally not configurable.
/// </summary>
public class ClinicSecurityPolicy
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClinicId { get; set; }
    public MfaEnforcementMode MfaEnforcementMode { get; set; }
    public DateTime? MfaEffectiveAtUtc { get; set; }
    public bool RequirePinChangeOnFirstLogin { get; set; } = true;
    public int MinimumPinLength { get; set; } = 8;
    public int SessionInactivityMinutes { get; set; } = 15;
    public bool AllowRoleCustomization { get; set; } = true;
    public bool RestrictCliniciansToOwnSchedules { get; set; }
    public AuthorizationRolloutMode AuthorizationMode { get; set; } = AuthorizationRolloutMode.Static;
    public long Version { get; set; } = 1;
    public Guid UpdatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public Clinic? Clinic { get; set; }
}

/// <summary>Encrypted TOTP enrollment data. The secret is never returned from persistence APIs.</summary>
public class UserMfaCredential
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string EncryptedSecret { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public long LastAcceptedTimeStep { get; set; } = -1;
    public int FailedAttemptCount { get; set; }
    public DateTime? LockedUntilUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ActivatedAtUtc { get; set; }
    public DateTime? ResetAtUtc { get; set; }
    public Guid? ResetByUserId { get; set; }
    public User? User { get; set; }
}

/// <summary>Hashed, single-use MFA recovery code.</summary>
public class UserMfaRecoveryCode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserMfaCredentialId { get; set; }
    public string CodeHash { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UsedAtUtc { get; set; }
    public UserMfaCredential? Credential { get; set; }
}

public class VisitType
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClinicId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    public bool RequiresIntake { get; set; }
    public bool PtaAllowed { get; set; }
    public bool IsBillable { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
    public long Version { get; set; } = 1;
    public Guid UpdatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public Clinic? Clinic { get; set; }
}

public class SchedulingPreferences
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClinicId { get; set; }
    public int DefaultAppointmentDurationMinutes { get; set; } = 45;
    public int AppointmentBufferMinutes { get; set; } = 15;
    public bool AllowDoubleBooking { get; set; }
    public bool AutoConfirmAppointments { get; set; } = true;
    public bool EnableClickToCreate { get; set; } = true;
    public bool ShowIntakeStatus { get; set; } = true;
    public bool AllowCancelFromWeekView { get; set; } = true;
    public bool AllowRescheduleFromWeekView { get; set; } = true;
    public string DefaultClinicianView { get; set; } = "Week";
    public string DefaultAdminView { get; set; } = "AllDay";
    public string? IntakeSentColor { get; set; }
    public string? IntakeIncompleteColor { get; set; }
    public string? IntakeCompleteColor { get; set; }
    public bool SendAppointmentReminders { get; set; } = true;
    public int ReminderLeadHours { get; set; } = 24;
    public long Version { get; set; } = 1;
    public Guid UpdatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public Clinic? Clinic { get; set; }
}

public class ClinicBusinessHour
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClinicId { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public bool IsOpen { get; set; }
    public TimeOnly? StartLocalTime { get; set; }
    public TimeOnly? EndLocalTime { get; set; }
    public TimeOnly? LunchStartLocalTime { get; set; }
    public TimeOnly? LunchEndLocalTime { get; set; }
    public long Version { get; set; } = 1;
    public Guid UpdatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public Clinic? Clinic { get; set; }
}

public class ScheduleBlockRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClinicId { get; set; }
    public Guid? ClinicianId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ReasonCode { get; set; } = string.Empty;
    public WeekdayFlags Weekdays { get; set; }
    public TimeOnly StartLocalTime { get; set; }
    public TimeOnly EndLocalTime { get; set; }
    public DateOnly EffectiveStartDate { get; set; }
    public DateOnly? EffectiveEndDate { get; set; }
    public bool IsRecurring { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public long Version { get; set; } = 1;
    public Guid UpdatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public Clinic? Clinic { get; set; }
}

public class AppointmentReminderDispatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClinicId { get; set; }
    public Guid AppointmentId { get; set; }
    public DateTime AppointmentVersionUtc { get; set; }
    public int ReminderLeadHours { get; set; }
    public ReminderDispatchPurpose Purpose { get; set; }
    public ReminderChannel Channel { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public ReminderDispatchStatus Status { get; set; }
    public int AttemptCount { get; set; }
    public DateTime EligibleAtUtc { get; set; }
    public DateTime? NextAttemptAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? LastStatusCode { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public Clinic? Clinic { get; set; }
    public Appointment? Appointment { get; set; }
}

public class AutoCheckInPolicy
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClinicId { get; set; }
    public bool IsEnabled { get; set; }
    public int LeadHours { get; set; } = 24;
    public bool EnableEmail { get; set; } = true;
    public bool EnableSms { get; set; } = true;
    public string TemplateKey { get; set; } = "default-intake-invite";
    public int MaxAttempts { get; set; } = 3;
    public string EligibleVisitTypeIdsJson { get; set; } = "[]";
    public long Version { get; set; } = 1;
    public Guid UpdatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public Clinic? Clinic { get; set; }
}

public class KioskStation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClinicId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DeviceCredentialHash { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public long Version { get; set; } = 1;
    public Guid UpdatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastSeenAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public Clinic? Clinic { get; set; }
}

public class KioskEnrollmentCode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClinicId { get; set; }
    public Guid KioskStationId { get; set; }
    public string CodeHash { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public KioskStation? KioskStation { get; set; }
}

public class KioskCheckInToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClinicId { get; set; }
    public Guid AppointmentId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? ConsumedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public Appointment? Appointment { get; set; }
}

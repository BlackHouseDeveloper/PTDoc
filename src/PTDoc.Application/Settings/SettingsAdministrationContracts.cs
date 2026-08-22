using PTDoc.Core.Models;

namespace PTDoc.Application.Settings;

public enum SettingsOperationStatus
{
    Succeeded = 0,
    ValidationFailed = 1,
    Conflict = 2,
    NotFound = 3,
    Forbidden = 4
}

public sealed record SettingsOperationResult<T>(
    SettingsOperationStatus Status,
    T? Value = default,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null,
    string? ErrorCode = null)
{
    public bool Succeeded => Status == SettingsOperationStatus.Succeeded;

    public static SettingsOperationResult<T> Success(T value) =>
        new(SettingsOperationStatus.Succeeded, value);

    public static SettingsOperationResult<T> Validation(IReadOnlyDictionary<string, string[]> errors) =>
        new(SettingsOperationStatus.ValidationFailed, ValidationErrors: errors, ErrorCode: "validation_failed");

    public static SettingsOperationResult<T> Conflict(string errorCode = "version_conflict") =>
        new(SettingsOperationStatus.Conflict, ErrorCode: errorCode);

    public static SettingsOperationResult<T> NotFound() =>
        new(SettingsOperationStatus.NotFound, ErrorCode: "not_found");

    public static SettingsOperationResult<T> Forbidden(string errorCode = "forbidden") =>
        new(SettingsOperationStatus.Forbidden, ErrorCode: errorCode);
}

public sealed record RolePermissionItem(
    CapabilityKey CapabilityKey,
    string Name,
    string Description,
    PermissionLevel Level,
    PermissionLevel LockedMinimum,
    bool IsSupported,
    long Version);

public sealed record RolePermissionSet(
    string RoleKey,
    string DisplayName,
    bool IsReadOnly,
    IReadOnlyList<RolePermissionItem> Permissions,
    int NoneCount,
    int ViewCount,
    int EditCount,
    int FullCount);

public sealed record RolePermissionsResponse(
    IReadOnlyList<RolePermissionSet> Roles,
    AuthorizationRolloutMode AuthorizationMode);

public sealed record PermissionUpdate(
    CapabilityKey CapabilityKey,
    PermissionLevel Level,
    long ExpectedVersion);

public sealed record UpdateRolePermissionsRequest(IReadOnlyList<PermissionUpdate> Permissions);

public sealed record CloneRolePermissionsRequest(string SourceRoleKey);

public interface IRolePermissionAdministrationService
{
    Task<RolePermissionsResponse> GetAsync(Guid clinicId, CancellationToken cancellationToken = default);

    Task<SettingsOperationResult<RolePermissionSet>> UpdateAsync(
        Guid clinicId,
        string roleKey,
        UpdateRolePermissionsRequest request,
        Guid actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<SettingsOperationResult<RolePermissionSet>> CloneAsync(
        Guid clinicId,
        string targetRoleKey,
        CloneRolePermissionsRequest request,
        Guid actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default);
}

public sealed record PermissionEvaluation(
    bool StaticAllowed,
    bool DynamicAllowed,
    bool EffectiveAllowed,
    AuthorizationRolloutMode Mode,
    string ReasonCode);

public interface IPermissionEvaluator
{
    Task<PermissionEvaluation> EvaluateAsync(
        Guid clinicId,
        string roleKey,
        CapabilityKey capabilityKey,
        PermissionLevel requiredLevel,
        bool staticAllowed,
        CancellationToken cancellationToken = default);
}

public sealed record SecurityPolicyDto(
    MfaEnforcementMode MfaEnforcementMode,
    DateTime? MfaEffectiveAtUtc,
    bool RequirePinChangeOnFirstLogin,
    int MinimumPinLength,
    int SessionInactivityMinutes,
    bool AllowRoleCustomization,
    bool RestrictCliniciansToOwnSchedules,
    AuthorizationRolloutMode AuthorizationMode,
    bool AuditLogEnabled,
    long Version);

public sealed record UpdateSecurityPolicyRequest(
    MfaEnforcementMode MfaEnforcementMode,
    DateTime? MfaEffectiveAtUtc,
    bool RequirePinChangeOnFirstLogin,
    int MinimumPinLength,
    int SessionInactivityMinutes,
    bool AllowRoleCustomization,
    bool RestrictCliniciansToOwnSchedules,
    AuthorizationRolloutMode AuthorizationMode,
    long ExpectedVersion);

public sealed record MfaReadinessDto(int ActiveUsers, int EnrolledUsers, int Administrators, int EnrolledAdministrators);

public interface ISecurityPolicyAdministrationService
{
    Task<SecurityPolicyDto> GetAsync(Guid clinicId, CancellationToken cancellationToken = default);

    Task<SettingsOperationResult<SecurityPolicyDto>> UpdateAsync(
        Guid clinicId,
        UpdateSecurityPolicyRequest request,
        Guid actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<MfaReadinessDto> GetMfaReadinessAsync(Guid clinicId, CancellationToken cancellationToken = default);

    Task<SettingsOperationResult<bool>> ForcePinChangeAsync(
        Guid clinicId,
        Guid userId,
        Guid actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<SettingsOperationResult<bool>> ResetMfaAsync(
        Guid clinicId,
        Guid userId,
        Guid actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default);
}

public sealed record MfaEnrollmentStart(string ManualKey, string OtpAuthUri, string QrSvg, string EnrollmentChallengeToken);

public sealed record MfaEnrollmentCompletion(IReadOnlyList<string> RecoveryCodes, string CompletionToken);

public sealed record MfaRecoveryCodeSet(IReadOnlyList<string> RecoveryCodes);

public sealed record MfaVerificationResult(bool Succeeded, string? CompletionToken, string? ErrorCode = null);

public enum MfaChallengePurpose
{
    Enrollment = 0,
    Verification = 1,
    PinChange = 2,
    AuthenticationCompletion = 3
}

public sealed record MfaChallengePrincipal(Guid UserId, MfaChallengePurpose Purpose);

public interface ISettingsSecretProtector
{
    string Protect(string purpose, string plaintext);
    bool TryUnprotect(string purpose, string protectedValue, TimeSpan maximumAge, out string plaintext);
}

public interface IMfaAuthenticationService
{
    string CreateChallenge(Guid userId, MfaChallengePurpose purpose);

    bool TryValidateChallenge(
        string challengeToken,
        MfaChallengePurpose purpose,
        TimeSpan maximumAge,
        out MfaChallengePrincipal principal);

    Task<SettingsOperationResult<MfaEnrollmentStart>> BeginEnrollmentAsync(
        string loginChallengeToken,
        CancellationToken cancellationToken = default);

    Task<SettingsOperationResult<MfaEnrollmentCompletion>> VerifyEnrollmentAsync(
        string enrollmentChallengeToken,
        string code,
        CancellationToken cancellationToken = default);

    Task<MfaVerificationResult> VerifyAsync(string challengeToken, string code, CancellationToken cancellationToken = default);

    Task<MfaVerificationResult> RecoverAsync(string challengeToken, string recoveryCode, CancellationToken cancellationToken = default);

    Task<SettingsOperationResult<MfaRecoveryCodeSet>> RegenerateRecoveryCodesAsync(
        Guid userId,
        string currentTotpCode,
        CancellationToken cancellationToken = default);
}

public sealed record VisitTypeDto(
    Guid Id,
    string Code,
    string Name,
    int DurationMinutes,
    bool RequiresIntake,
    bool PtaAllowed,
    bool IsBillable,
    bool IsActive,
    int DisplayOrder,
    long Version);

public sealed record SaveVisitTypeRequest(
    string Code,
    string Name,
    int DurationMinutes,
    bool RequiresIntake,
    bool PtaAllowed,
    bool IsBillable,
    bool IsActive,
    int DisplayOrder,
    long? ExpectedVersion = null);

public sealed record SchedulingPreferencesDto(
    int DefaultAppointmentDurationMinutes,
    int AppointmentBufferMinutes,
    bool AllowDoubleBooking,
    bool AutoConfirmAppointments,
    bool EnableClickToCreate,
    bool ShowIntakeStatus,
    bool AllowCancelFromWeekView,
    bool AllowRescheduleFromWeekView,
    string DefaultClinicianView,
    string DefaultAdminView,
    string? IntakeSentColor,
    string? IntakeIncompleteColor,
    string? IntakeCompleteColor,
    bool SendAppointmentReminders,
    int ReminderLeadHours,
    long Version);

public sealed record UpdateSchedulingPreferencesRequest(
    int DefaultAppointmentDurationMinutes,
    int AppointmentBufferMinutes,
    bool AllowDoubleBooking,
    bool AutoConfirmAppointments,
    bool EnableClickToCreate,
    bool ShowIntakeStatus,
    bool AllowCancelFromWeekView,
    bool AllowRescheduleFromWeekView,
    string DefaultClinicianView,
    string DefaultAdminView,
    string? IntakeSentColor,
    string? IntakeIncompleteColor,
    string? IntakeCompleteColor,
    bool SendAppointmentReminders,
    int ReminderLeadHours,
    long ExpectedVersion);

public sealed record ClinicBusinessHourDto(
    Guid Id,
    DayOfWeek DayOfWeek,
    bool IsOpen,
    TimeOnly? StartLocalTime,
    TimeOnly? EndLocalTime,
    TimeOnly? LunchStartLocalTime,
    TimeOnly? LunchEndLocalTime,
    long Version);

public sealed record SaveClinicBusinessHourRequest(
    DayOfWeek DayOfWeek,
    bool IsOpen,
    TimeOnly? StartLocalTime,
    TimeOnly? EndLocalTime,
    TimeOnly? LunchStartLocalTime,
    TimeOnly? LunchEndLocalTime,
    long ExpectedVersion);

public sealed record ClinicHoursDto(
    string TimeZoneId,
    long ClinicVersion,
    IReadOnlyList<ClinicBusinessHourDto> Hours);

public sealed record UpdateClinicHoursRequest(
    string TimeZoneId,
    long ExpectedClinicVersion,
    IReadOnlyList<SaveClinicBusinessHourRequest> Hours);

public sealed record ScheduleBlockDto(
    Guid Id,
    Guid? ClinicianId,
    string Name,
    string ReasonCode,
    WeekdayFlags Weekdays,
    TimeOnly StartLocalTime,
    TimeOnly EndLocalTime,
    DateOnly EffectiveStartDate,
    DateOnly? EffectiveEndDate,
    bool IsRecurring,
    bool IsActive,
    long Version);

public sealed record SaveScheduleBlockRequest(
    Guid? ClinicianId,
    string Name,
    string ReasonCode,
    WeekdayFlags Weekdays,
    TimeOnly StartLocalTime,
    TimeOnly EndLocalTime,
    DateOnly EffectiveStartDate,
    DateOnly? EffectiveEndDate,
    bool IsRecurring,
    bool IsActive,
    long? ExpectedVersion = null);

public interface ISchedulingAdministrationService
{
    Task<IReadOnlyList<VisitTypeDto>> GetVisitTypesAsync(Guid clinicId, bool includeInactive, CancellationToken cancellationToken = default);
    Task<SettingsOperationResult<VisitTypeDto>> CreateVisitTypeAsync(Guid clinicId, SaveVisitTypeRequest request, Guid actorUserId, string correlationId, CancellationToken cancellationToken = default);
    Task<SettingsOperationResult<VisitTypeDto>> UpdateVisitTypeAsync(Guid clinicId, Guid visitTypeId, SaveVisitTypeRequest request, Guid actorUserId, string correlationId, CancellationToken cancellationToken = default);
    Task<SettingsOperationResult<VisitTypeDto>> DeactivateVisitTypeAsync(Guid clinicId, Guid visitTypeId, long expectedVersion, Guid actorUserId, string correlationId, CancellationToken cancellationToken = default);
    Task<SchedulingPreferencesDto> GetPreferencesAsync(Guid clinicId, CancellationToken cancellationToken = default);
    Task<SettingsOperationResult<SchedulingPreferencesDto>> UpdatePreferencesAsync(Guid clinicId, UpdateSchedulingPreferencesRequest request, Guid actorUserId, string correlationId, CancellationToken cancellationToken = default);
    Task<ClinicHoursDto> GetClinicHoursAsync(Guid clinicId, CancellationToken cancellationToken = default);
    Task<SettingsOperationResult<ClinicHoursDto>> UpdateClinicHoursAsync(Guid clinicId, UpdateClinicHoursRequest request, Guid actorUserId, string correlationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ScheduleBlockDto>> GetScheduleBlocksAsync(Guid clinicId, CancellationToken cancellationToken = default);
    Task<SettingsOperationResult<ScheduleBlockDto>> CreateScheduleBlockAsync(Guid clinicId, SaveScheduleBlockRequest request, Guid actorUserId, string correlationId, CancellationToken cancellationToken = default);
    Task<SettingsOperationResult<ScheduleBlockDto>> UpdateScheduleBlockAsync(Guid clinicId, Guid blockId, SaveScheduleBlockRequest request, Guid actorUserId, string correlationId, CancellationToken cancellationToken = default);
    Task<SettingsOperationResult<ScheduleBlockDto>> DeactivateScheduleBlockAsync(Guid clinicId, Guid blockId, long expectedVersion, Guid actorUserId, string correlationId, CancellationToken cancellationToken = default);
}

public sealed record AvailabilityRequest(Guid ClinicId, Guid ClinicianId, DateTime StartUtc, DateTime EndUtc, Guid? ExcludingAppointmentId = null);

public sealed record AvailabilityDecision(bool IsAvailable, bool RequiresAuthorizedOverlap, IReadOnlyList<string> ReasonCodes);

public interface ISchedulingPolicyEvaluator
{
    Task<AvailabilityDecision> EvaluateAsync(AvailabilityRequest request, CancellationToken cancellationToken = default);
}

public sealed record AutoCheckInPolicyDto(
    bool IsEnabled,
    int LeadHours,
    bool EnableEmail,
    bool EnableSms,
    string TemplateKey,
    int MaxAttempts,
    IReadOnlyList<Guid> EligibleVisitTypeIds,
    long Version);

public sealed record UpdateAutoCheckInPolicyRequest(
    bool IsEnabled,
    int LeadHours,
    bool EnableEmail,
    bool EnableSms,
    string TemplateKey,
    int MaxAttempts,
    IReadOnlyList<Guid> EligibleVisitTypeIds,
    long ExpectedVersion);

public interface IAutoCheckInAdministrationService
{
    Task<AutoCheckInPolicyDto> GetAsync(Guid clinicId, CancellationToken cancellationToken = default);
    Task<SettingsOperationResult<AutoCheckInPolicyDto>> UpdateAsync(Guid clinicId, UpdateAutoCheckInPolicyRequest request, Guid actorUserId, string correlationId, CancellationToken cancellationToken = default);
}

public sealed record KioskStationDto(Guid Id, string Name, bool IsActive, DateTime? LastSeenAtUtc, long Version);
public sealed record CreateKioskStationRequest(string Name);
public sealed record UpdateKioskStationRequest(string Name, bool IsActive, long ExpectedVersion);
public sealed record KioskEnrollmentCodeDto(Guid StationId, string Code, DateTime ExpiresAtUtc);
public sealed record KioskEnrollmentResult(Guid StationId, string StationName, string DeviceCredential);
public sealed record KioskCheckInTokenDto(Guid AppointmentId, string NumericCode, string QrPayload, DateTime ExpiresAtUtc);
public sealed record KioskCheckInResult(Guid AppointmentId, DateTime CheckedInAtUtc);

public interface IKioskCheckInService
{
    Task<IReadOnlyList<KioskStationDto>> GetStationsAsync(Guid clinicId, CancellationToken cancellationToken = default);
    Task<SettingsOperationResult<KioskEnrollmentCodeDto>> CreateStationAsync(Guid clinicId, CreateKioskStationRequest request, Guid actorUserId, string correlationId, CancellationToken cancellationToken = default);
    Task<SettingsOperationResult<KioskStationDto>> UpdateStationAsync(Guid clinicId, Guid stationId, UpdateKioskStationRequest request, Guid actorUserId, string correlationId, CancellationToken cancellationToken = default);
    Task<SettingsOperationResult<KioskEnrollmentCodeDto>> RotateEnrollmentAsync(Guid clinicId, Guid stationId, long expectedVersion, Guid actorUserId, string correlationId, CancellationToken cancellationToken = default);
    Task<SettingsOperationResult<bool>> RevokeStationAsync(Guid clinicId, Guid stationId, long expectedVersion, Guid actorUserId, string correlationId, CancellationToken cancellationToken = default);
    Task<SettingsOperationResult<KioskEnrollmentResult>> EnrollAsync(string enrollmentCode, CancellationToken cancellationToken = default);
    Task<SettingsOperationResult<KioskCheckInTokenDto>> CreateCheckInTokenAsync(Guid clinicId, Guid appointmentId, Guid actorUserId, string correlationId, CancellationToken cancellationToken = default);
    Task<SettingsOperationResult<KioskCheckInResult>> CheckInAsync(string deviceCredential, string appointmentToken, CancellationToken cancellationToken = default);
}

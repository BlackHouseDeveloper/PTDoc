using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Compliance;
using PTDoc.Application.Settings;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Infrastructure.Settings;

public sealed partial class SchedulingAdministrationService(
    ApplicationDbContext context,
    IAuditService auditService) : ISchedulingAdministrationService
{
    public async Task<IReadOnlyList<VisitTypeDto>> GetVisitTypesAsync(
        Guid clinicId,
        bool includeInactive,
        CancellationToken cancellationToken = default) =>
        await context.VisitTypes
            .Where(item => item.ClinicId == clinicId && (includeInactive || item.IsActive))
            .OrderBy(item => item.DisplayOrder)
            .ThenBy(item => item.Name)
            .Select(item => MapVisitType(item))
            .ToListAsync(cancellationToken);

    public async Task<SettingsOperationResult<VisitTypeDto>> CreateVisitTypeAsync(
        Guid clinicId,
        SaveVisitTypeRequest request,
        Guid actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var errors = ValidateVisitType(request, requireExpectedVersion: false);
        if (errors.Count > 0)
        {
            return SettingsOperationResult<VisitTypeDto>.Validation(errors);
        }

        var normalizedCode = NormalizeCode(request.Code);
        if (await context.VisitTypes.AnyAsync(
                item => item.ClinicId == clinicId && item.Code == normalizedCode,
                cancellationToken))
        {
            return SettingsOperationResult<VisitTypeDto>.Validation(
                new Dictionary<string, string[]> { ["code"] = ["A visit type with this code already exists."] });
        }

        var entity = new VisitType
        {
            ClinicId = clinicId,
            Code = normalizedCode,
            Name = request.Name.Trim(),
            DurationMinutes = request.DurationMinutes,
            RequiresIntake = request.RequiresIntake,
            PtaAllowed = request.PtaAllowed,
            IsBillable = request.IsBillable,
            IsActive = request.IsActive,
            DisplayOrder = request.DisplayOrder,
            UpdatedByUserId = actorUserId
        };
        context.VisitTypes.Add(entity);
        await context.SaveChangesAsync(cancellationToken);
        await AuditAsync("VisitTypeCreated", clinicId, nameof(VisitType), entity.Id, actorUserId, correlationId,
            new() { ["visitTypeId"] = entity.Id, ["code"] = entity.Code }, cancellationToken);
        return SettingsOperationResult<VisitTypeDto>.Success(MapVisitType(entity));
    }

    public async Task<SettingsOperationResult<VisitTypeDto>> UpdateVisitTypeAsync(
        Guid clinicId,
        Guid visitTypeId,
        SaveVisitTypeRequest request,
        Guid actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var errors = ValidateVisitType(request, requireExpectedVersion: true);
        if (errors.Count > 0)
        {
            return SettingsOperationResult<VisitTypeDto>.Validation(errors);
        }

        var entity = await context.VisitTypes.SingleOrDefaultAsync(
            item => item.Id == visitTypeId && item.ClinicId == clinicId,
            cancellationToken);
        if (entity is null)
        {
            return SettingsOperationResult<VisitTypeDto>.NotFound();
        }

        if (entity.Version != request.ExpectedVersion)
        {
            return SettingsOperationResult<VisitTypeDto>.Conflict();
        }

        var normalizedCode = NormalizeCode(request.Code);
        if (await context.VisitTypes.AnyAsync(
                item => item.ClinicId == clinicId && item.Id != visitTypeId && item.Code == normalizedCode,
                cancellationToken))
        {
            return SettingsOperationResult<VisitTypeDto>.Validation(
                new Dictionary<string, string[]> { ["code"] = ["A visit type with this code already exists."] });
        }

        entity.Code = normalizedCode;
        entity.Name = request.Name.Trim();
        entity.DurationMinutes = request.DurationMinutes;
        entity.RequiresIntake = request.RequiresIntake;
        entity.PtaAllowed = request.PtaAllowed;
        entity.IsBillable = request.IsBillable;
        entity.IsActive = request.IsActive;
        entity.DisplayOrder = request.DisplayOrder;
        entity.Version++;
        entity.UpdatedByUserId = actorUserId;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        var result = await SaveWithConflictAsync(entity, cancellationToken);
        if (result is not null)
        {
            return result;
        }

        await AuditAsync("VisitTypeUpdated", clinicId, nameof(VisitType), entity.Id, actorUserId, correlationId,
            new() { ["visitTypeId"] = entity.Id, ["version"] = entity.Version }, cancellationToken);
        return SettingsOperationResult<VisitTypeDto>.Success(MapVisitType(entity));
    }

    public async Task<SettingsOperationResult<VisitTypeDto>> DeactivateVisitTypeAsync(
        Guid clinicId,
        Guid visitTypeId,
        long expectedVersion,
        Guid actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var entity = await context.VisitTypes.SingleOrDefaultAsync(
            item => item.Id == visitTypeId && item.ClinicId == clinicId,
            cancellationToken);
        if (entity is null)
        {
            return SettingsOperationResult<VisitTypeDto>.NotFound();
        }

        if (entity.Version != expectedVersion)
        {
            return SettingsOperationResult<VisitTypeDto>.Conflict();
        }

        entity.IsActive = false;
        entity.Version++;
        entity.UpdatedByUserId = actorUserId;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        var conflict = await SaveWithConflictAsync(entity, cancellationToken);
        if (conflict is not null)
        {
            return conflict;
        }

        await AuditAsync("VisitTypeDeactivated", clinicId, nameof(VisitType), entity.Id, actorUserId, correlationId,
            new() { ["visitTypeId"] = entity.Id, ["version"] = entity.Version }, cancellationToken);
        return SettingsOperationResult<VisitTypeDto>.Success(MapVisitType(entity));
    }

    public async Task<SchedulingPreferencesDto> GetPreferencesAsync(
        Guid clinicId,
        CancellationToken cancellationToken = default)
    {
        var preferences = await context.SchedulingPreferences
            .SingleOrDefaultAsync(item => item.ClinicId == clinicId, cancellationToken);
        return preferences is null ? DefaultPreferences() : MapPreferences(preferences);
    }

    public async Task<SettingsOperationResult<SchedulingPreferencesDto>> UpdatePreferencesAsync(
        Guid clinicId,
        UpdateSchedulingPreferencesRequest request,
        Guid actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var errors = ValidatePreferences(request);
        if (errors.Count > 0)
        {
            return SettingsOperationResult<SchedulingPreferencesDto>.Validation(errors);
        }

        var preferences = await context.SchedulingPreferences
            .SingleOrDefaultAsync(item => item.ClinicId == clinicId, cancellationToken);
        if ((preferences?.Version ?? 0) != request.ExpectedVersion)
        {
            return SettingsOperationResult<SchedulingPreferencesDto>.Conflict();
        }

        if (preferences is null)
        {
            preferences = new SchedulingPreferences
            {
                ClinicId = clinicId,
                UpdatedByUserId = actorUserId
            };
            context.SchedulingPreferences.Add(preferences);
        }
        else
        {
            preferences.Version++;
            preferences.UpdatedByUserId = actorUserId;
            preferences.UpdatedAtUtc = DateTime.UtcNow;
        }

        preferences.DefaultAppointmentDurationMinutes = request.DefaultAppointmentDurationMinutes;
        preferences.AppointmentBufferMinutes = request.AppointmentBufferMinutes;
        preferences.AllowDoubleBooking = request.AllowDoubleBooking;
        preferences.AutoConfirmAppointments = request.AutoConfirmAppointments;
        preferences.EnableClickToCreate = request.EnableClickToCreate;
        preferences.ShowIntakeStatus = request.ShowIntakeStatus;
        preferences.AllowCancelFromWeekView = request.AllowCancelFromWeekView;
        preferences.AllowRescheduleFromWeekView = request.AllowRescheduleFromWeekView;
        preferences.DefaultClinicianView = request.DefaultClinicianView;
        preferences.DefaultAdminView = request.DefaultAdminView;
        preferences.IntakeSentColor = NormalizeColor(request.IntakeSentColor);
        preferences.IntakeIncompleteColor = NormalizeColor(request.IntakeIncompleteColor);
        preferences.IntakeCompleteColor = NormalizeColor(request.IntakeCompleteColor);
        preferences.SendAppointmentReminders = request.SendAppointmentReminders;
        preferences.ReminderLeadHours = request.ReminderLeadHours;

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return SettingsOperationResult<SchedulingPreferencesDto>.Conflict();
        }

        await AuditAsync("SchedulingPreferencesUpdated", clinicId, nameof(SchedulingPreferences), preferences.Id,
            actorUserId, correlationId, new() { ["version"] = preferences.Version }, cancellationToken);
        return SettingsOperationResult<SchedulingPreferencesDto>.Success(MapPreferences(preferences));
    }

    public async Task<ClinicHoursDto> GetClinicHoursAsync(Guid clinicId, CancellationToken cancellationToken = default)
    {
        var clinic = await context.Clinics.SingleOrDefaultAsync(item => item.Id == clinicId, cancellationToken);
        var hours = await context.ClinicBusinessHours
            .Where(item => item.ClinicId == clinicId)
            .OrderBy(item => item.DayOfWeek)
            .Select(item => MapHour(item))
            .ToListAsync(cancellationToken);
        return new ClinicHoursDto(clinic?.TimeZoneId ?? "America/Los_Angeles", clinic?.Version ?? 0, hours);
    }

    public async Task<SettingsOperationResult<ClinicHoursDto>> UpdateClinicHoursAsync(
        Guid clinicId,
        UpdateClinicHoursRequest request,
        Guid actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var errors = ValidateClinicHours(request);
        if (errors.Count > 0)
        {
            return SettingsOperationResult<ClinicHoursDto>.Validation(errors);
        }

        var clinic = await context.Clinics.SingleOrDefaultAsync(item => item.Id == clinicId, cancellationToken);
        if (clinic is null)
        {
            return SettingsOperationResult<ClinicHoursDto>.NotFound();
        }

        if (clinic.Version != request.ExpectedClinicVersion)
        {
            return SettingsOperationResult<ClinicHoursDto>.Conflict();
        }

        var existing = await context.ClinicBusinessHours
            .Where(item => item.ClinicId == clinicId)
            .ToDictionaryAsync(item => item.DayOfWeek, cancellationToken);

        foreach (var update in request.Hours)
        {
            existing.TryGetValue(update.DayOfWeek, out var hour);
            if ((hour?.Version ?? 0) != update.ExpectedVersion)
            {
                return SettingsOperationResult<ClinicHoursDto>.Conflict();
            }

            if (hour is null)
            {
                hour = new ClinicBusinessHour
                {
                    ClinicId = clinicId,
                    DayOfWeek = update.DayOfWeek,
                    UpdatedByUserId = actorUserId
                };
                context.ClinicBusinessHours.Add(hour);
                existing[update.DayOfWeek] = hour;
            }
            else
            {
                hour.Version++;
                hour.UpdatedByUserId = actorUserId;
                hour.UpdatedAtUtc = DateTime.UtcNow;
            }

            hour.IsOpen = update.IsOpen;
            hour.StartLocalTime = update.IsOpen ? update.StartLocalTime : null;
            hour.EndLocalTime = update.IsOpen ? update.EndLocalTime : null;
            hour.LunchStartLocalTime = update.IsOpen ? update.LunchStartLocalTime : null;
            hour.LunchEndLocalTime = update.IsOpen ? update.LunchEndLocalTime : null;
        }

        clinic.TimeZoneId = request.TimeZoneId;
        clinic.Version++;
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return SettingsOperationResult<ClinicHoursDto>.Conflict();
        }

        await AuditAsync("ClinicHoursUpdated", clinicId, nameof(ClinicBusinessHour), clinicId,
            actorUserId, correlationId,
            new() { ["clinicVersion"] = clinic.Version, ["timeZoneId"] = clinic.TimeZoneId },
            cancellationToken);

        return SettingsOperationResult<ClinicHoursDto>.Success(new ClinicHoursDto(
            clinic.TimeZoneId,
            clinic.Version,
            existing.Values.OrderBy(item => item.DayOfWeek).Select(MapHour).ToArray()));
    }

    public async Task<IReadOnlyList<ScheduleBlockDto>> GetScheduleBlocksAsync(
        Guid clinicId,
        CancellationToken cancellationToken = default) =>
        await context.ScheduleBlockRules
            .Where(item => item.ClinicId == clinicId && item.IsActive)
            .OrderBy(item => item.Name)
            .Select(item => MapBlock(item))
            .ToListAsync(cancellationToken);

    public async Task<SettingsOperationResult<ScheduleBlockDto>> CreateScheduleBlockAsync(
        Guid clinicId,
        SaveScheduleBlockRequest request,
        Guid actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var errors = ValidateBlock(request, requireExpectedVersion: false);
        if (errors.Count > 0)
        {
            return SettingsOperationResult<ScheduleBlockDto>.Validation(errors);
        }

        var entity = new ScheduleBlockRule
        {
            ClinicId = clinicId,
            ClinicianId = request.ClinicianId,
            Name = request.Name.Trim(),
            ReasonCode = request.ReasonCode.Trim(),
            Weekdays = request.Weekdays,
            StartLocalTime = request.StartLocalTime,
            EndLocalTime = request.EndLocalTime,
            EffectiveStartDate = request.EffectiveStartDate,
            EffectiveEndDate = request.EffectiveEndDate,
            IsRecurring = request.IsRecurring,
            IsActive = request.IsActive,
            UpdatedByUserId = actorUserId
        };
        context.ScheduleBlockRules.Add(entity);
        await context.SaveChangesAsync(cancellationToken);
        await AuditAsync("ScheduleBlockCreated", clinicId, nameof(ScheduleBlockRule), entity.Id,
            actorUserId, correlationId, new() { ["blockId"] = entity.Id }, cancellationToken);
        return SettingsOperationResult<ScheduleBlockDto>.Success(MapBlock(entity));
    }

    public async Task<SettingsOperationResult<ScheduleBlockDto>> UpdateScheduleBlockAsync(
        Guid clinicId,
        Guid blockId,
        SaveScheduleBlockRequest request,
        Guid actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var errors = ValidateBlock(request, requireExpectedVersion: true);
        if (errors.Count > 0)
        {
            return SettingsOperationResult<ScheduleBlockDto>.Validation(errors);
        }

        var entity = await context.ScheduleBlockRules.SingleOrDefaultAsync(
            item => item.Id == blockId && item.ClinicId == clinicId,
            cancellationToken);
        if (entity is null)
        {
            return SettingsOperationResult<ScheduleBlockDto>.NotFound();
        }

        if (entity.Version != request.ExpectedVersion)
        {
            return SettingsOperationResult<ScheduleBlockDto>.Conflict();
        }

        entity.ClinicianId = request.ClinicianId;
        entity.Name = request.Name.Trim();
        entity.ReasonCode = request.ReasonCode.Trim();
        entity.Weekdays = request.Weekdays;
        entity.StartLocalTime = request.StartLocalTime;
        entity.EndLocalTime = request.EndLocalTime;
        entity.EffectiveStartDate = request.EffectiveStartDate;
        entity.EffectiveEndDate = request.EffectiveEndDate;
        entity.IsRecurring = request.IsRecurring;
        entity.IsActive = request.IsActive;
        entity.Version++;
        entity.UpdatedByUserId = actorUserId;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return SettingsOperationResult<ScheduleBlockDto>.Conflict();
        }

        await AuditAsync("ScheduleBlockUpdated", clinicId, nameof(ScheduleBlockRule), entity.Id,
            actorUserId, correlationId, new() { ["blockId"] = entity.Id, ["version"] = entity.Version }, cancellationToken);
        return SettingsOperationResult<ScheduleBlockDto>.Success(MapBlock(entity));
    }

    public async Task<SettingsOperationResult<ScheduleBlockDto>> DeactivateScheduleBlockAsync(
        Guid clinicId,
        Guid blockId,
        long expectedVersion,
        Guid actorUserId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var entity = await context.ScheduleBlockRules.SingleOrDefaultAsync(
            item => item.Id == blockId && item.ClinicId == clinicId,
            cancellationToken);
        if (entity is null)
        {
            return SettingsOperationResult<ScheduleBlockDto>.NotFound();
        }

        if (entity.Version != expectedVersion)
        {
            return SettingsOperationResult<ScheduleBlockDto>.Conflict();
        }

        entity.IsActive = false;
        entity.Version++;
        entity.UpdatedByUserId = actorUserId;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return SettingsOperationResult<ScheduleBlockDto>.Conflict();
        }

        await AuditAsync("ScheduleBlockDeactivated", clinicId, nameof(ScheduleBlockRule), entity.Id,
            actorUserId, correlationId, new() { ["blockId"] = entity.Id, ["version"] = entity.Version }, cancellationToken);
        return SettingsOperationResult<ScheduleBlockDto>.Success(MapBlock(entity));
    }

    private async Task<SettingsOperationResult<VisitTypeDto>?> SaveWithConflictAsync(
        VisitType entity,
        CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (DbUpdateConcurrencyException)
        {
            return SettingsOperationResult<VisitTypeDto>.Conflict();
        }
    }

    private Task AuditAsync(
        string eventType,
        Guid clinicId,
        string entityType,
        Guid entityId,
        Guid actorUserId,
        string correlationId,
        Dictionary<string, object> metadata,
        CancellationToken cancellationToken)
    {
        metadata["clinicId"] = clinicId;
        return auditService.LogSettingsEventAsync(new AuditEvent
        {
            EventType = eventType,
            UserId = actorUserId,
            CorrelationId = correlationId,
            EntityType = entityType,
            EntityId = entityId,
            Metadata = metadata
        }, cancellationToken);
    }

    private static Dictionary<string, string[]> ValidateVisitType(SaveVisitTypeRequest request, bool requireExpectedVersion)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Code) || !CodePattern().IsMatch(NormalizeCode(request.Code)))
            errors["code"] = ["Code must contain only lowercase letters, numbers, and hyphens."];
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 160)
            errors["name"] = ["Name is required and cannot exceed 160 characters."];
        if (request.DurationMinutes is < 0 or > 480)
            errors["durationMinutes"] = ["Duration must be between 0 and 480 minutes."];
        if (request.DisplayOrder < 0)
            errors["displayOrder"] = ["Display order cannot be negative."];
        if (requireExpectedVersion && request.ExpectedVersion is null)
            errors["expectedVersion"] = ["ExpectedVersion is required for updates."];
        return errors;
    }

    private static Dictionary<string, string[]> ValidatePreferences(UpdateSchedulingPreferencesRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.DefaultAppointmentDurationMinutes is < 5 or > 480)
            errors["defaultAppointmentDurationMinutes"] = ["Default appointment duration must be between 5 and 480 minutes."];
        if (request.AppointmentBufferMinutes is < 0 or > 120)
            errors["appointmentBufferMinutes"] = ["Appointment buffer must be between 0 and 120 minutes."];
        if (request.ReminderLeadHours is not (12 or 24 or 48))
            errors["reminderLeadHours"] = ["Reminder lead time must be 12, 24, or 48 hours."];
        ValidateColor(errors, "intakeSentColor", request.IntakeSentColor);
        ValidateColor(errors, "intakeIncompleteColor", request.IntakeIncompleteColor);
        ValidateColor(errors, "intakeCompleteColor", request.IntakeCompleteColor);
        return errors;
    }

    private static Dictionary<string, string[]> ValidateClinicHours(UpdateClinicHoursRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (!IsIanaTimeZone(request.TimeZoneId))
            errors["timeZoneId"] = ["A valid IANA time-zone identifier is required."];
        if (request.Hours.Count != 7 || request.Hours.Select(item => item.DayOfWeek).Distinct().Count() != 7)
            errors["hours"] = ["Exactly one clinic-hours row is required for each weekday."];

        foreach (var item in request.Hours)
        {
            var prefix = $"hours.{item.DayOfWeek}";
            if (item.IsOpen && (item.StartLocalTime is null || item.EndLocalTime is null || item.StartLocalTime >= item.EndLocalTime))
                errors[prefix] = ["Open days require a start time before the end time."];
            if ((item.LunchStartLocalTime is null) != (item.LunchEndLocalTime is null))
                errors[$"{prefix}.lunch"] = ["Lunch start and end must both be provided or both be blank."];
            if (item.LunchStartLocalTime is { } lunchStart &&
                item.LunchEndLocalTime is { } lunchEnd &&
                item.StartLocalTime is { } dayStart &&
                item.EndLocalTime is { } dayEnd &&
                (lunchStart >= lunchEnd || lunchStart < dayStart || lunchEnd > dayEnd))
                errors[$"{prefix}.lunch"] = ["Lunch must be within clinic hours and start before it ends."];
        }

        return errors;
    }

    private static Dictionary<string, string[]> ValidateBlock(SaveScheduleBlockRequest request, bool requireExpectedVersion)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 160)
            errors["name"] = ["Name is required and cannot exceed 160 characters."];
        if (string.IsNullOrWhiteSpace(request.ReasonCode) || request.ReasonCode.Trim().Length > 80)
            errors["reasonCode"] = ["A non-PHI reason code is required."];
        if (request.Weekdays == WeekdayFlags.None)
            errors["weekdays"] = ["At least one weekday is required."];
        if (request.StartLocalTime >= request.EndLocalTime)
            errors["endLocalTime"] = ["End time must be after start time."];
        if (request.EffectiveEndDate is { } effectiveEnd && effectiveEnd < request.EffectiveStartDate)
            errors["effectiveEndDate"] = ["End date cannot precede start date."];
        if (requireExpectedVersion && request.ExpectedVersion is null)
            errors["expectedVersion"] = ["ExpectedVersion is required for updates."];
        return errors;
    }

    private static void ValidateColor(IDictionary<string, string[]> errors, string field, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) && !ColorPattern().IsMatch(value.Trim()))
            errors[field] = ["Color must be blank or a six-digit CSS hex value such as #2563EB."];
    }

    private static bool IsIanaTimeZone(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || (value != "UTC" && !value.Contains('/')))
            return false;
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(value);
            return true;
        }
        catch (TimeZoneNotFoundException) { return false; }
        catch (InvalidTimeZoneException) { return false; }
    }

    private static string NormalizeCode(string value) => value.Trim().ToLowerInvariant().Replace(' ', '-');
    private static string? NormalizeColor(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static VisitTypeDto MapVisitType(VisitType item) => new(
        item.Id, item.Code, item.Name, item.DurationMinutes, item.RequiresIntake,
        item.PtaAllowed, item.IsBillable, item.IsActive, item.DisplayOrder, item.Version);

    private static SchedulingPreferencesDto MapPreferences(SchedulingPreferences item) => new(
        item.DefaultAppointmentDurationMinutes, item.AppointmentBufferMinutes, item.AllowDoubleBooking,
        item.AutoConfirmAppointments, item.EnableClickToCreate, item.ShowIntakeStatus,
        item.AllowCancelFromWeekView, item.AllowRescheduleFromWeekView, item.DefaultClinicianView,
        item.DefaultAdminView, item.IntakeSentColor, item.IntakeIncompleteColor, item.IntakeCompleteColor,
        item.SendAppointmentReminders, item.ReminderLeadHours, item.Version);

    private static SchedulingPreferencesDto DefaultPreferences() => MapPreferences(new SchedulingPreferences { Version = 0 });

    private static ClinicBusinessHourDto MapHour(ClinicBusinessHour item) => new(
        item.Id, item.DayOfWeek, item.IsOpen, item.StartLocalTime, item.EndLocalTime,
        item.LunchStartLocalTime, item.LunchEndLocalTime, item.Version);

    private static ScheduleBlockDto MapBlock(ScheduleBlockRule item) => new(
        item.Id, item.ClinicianId, item.Name, item.ReasonCode, item.Weekdays,
        item.StartLocalTime, item.EndLocalTime, item.EffectiveStartDate, item.EffectiveEndDate,
        item.IsRecurring, item.IsActive, item.Version);

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex CodePattern();

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex ColorPattern();
}

public sealed class SchedulingPolicyEvaluator(ApplicationDbContext context) : ISchedulingPolicyEvaluator
{
    public async Task<AvailabilityDecision> EvaluateAsync(
        AvailabilityRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.EndUtc <= request.StartUtc)
            return new AvailabilityDecision(false, false, ["invalid_interval"]);

        var clinic = await context.Clinics.SingleOrDefaultAsync(item => item.Id == request.ClinicId, cancellationToken);
        if (clinic is null)
            return new AvailabilityDecision(false, false, ["clinic_not_found"]);

        TimeZoneInfo timeZone;
        try { timeZone = TimeZoneInfo.FindSystemTimeZoneById(clinic.TimeZoneId); }
        catch (TimeZoneNotFoundException) { return new AvailabilityDecision(false, false, ["invalid_clinic_time_zone"]); }
        catch (InvalidTimeZoneException) { return new AvailabilityDecision(false, false, ["invalid_clinic_time_zone"]); }

        var localStart = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(request.StartUtc, DateTimeKind.Utc), timeZone);
        var localEnd = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(request.EndUtc, DateTimeKind.Utc), timeZone);
        if (localStart.Date != localEnd.Date)
            return new AvailabilityDecision(false, false, ["crosses_clinic_day"]);

        var localDate = DateOnly.FromDateTime(localStart);
        var startTime = TimeOnly.FromDateTime(localStart);
        var endTime = TimeOnly.FromDateTime(localEnd);
        var reasons = new List<string>();

        var hours = await context.ClinicBusinessHours.SingleOrDefaultAsync(
            item => item.ClinicId == request.ClinicId && item.DayOfWeek == localStart.DayOfWeek,
            cancellationToken);
        if (hours is null || !hours.IsOpen || hours.StartLocalTime is null || hours.EndLocalTime is null ||
            startTime < hours.StartLocalTime.Value || endTime > hours.EndLocalTime.Value)
            reasons.Add("outside_clinic_hours");
        if (hours?.LunchStartLocalTime is { } lunchStart &&
            hours.LunchEndLocalTime is { } lunchEnd &&
            startTime < lunchEnd && endTime > lunchStart)
            reasons.Add("overlaps_lunch");

        var weekday = ToFlag(localStart.DayOfWeek);
        var blocks = await context.ScheduleBlockRules
            .Where(item => item.ClinicId == request.ClinicId && item.IsActive &&
                           (item.ClinicianId == null || item.ClinicianId == request.ClinicianId) &&
                           item.EffectiveStartDate <= localDate &&
                           (item.EffectiveEndDate == null || item.EffectiveEndDate >= localDate))
            .ToListAsync(cancellationToken);
        if (blocks.Any(block =>
                (!block.IsRecurring || (block.Weekdays & weekday) != 0) &&
                startTime < block.EndLocalTime && endTime > block.StartLocalTime))
            reasons.Add("schedule_block");

        var preferences = await context.SchedulingPreferences.SingleOrDefaultAsync(
            item => item.ClinicId == request.ClinicId,
            cancellationToken);
        var bufferMinutes = preferences?.AppointmentBufferMinutes ?? 15;
        var bufferedStart = request.StartUtc.AddMinutes(-bufferMinutes);
        var bufferedEnd = request.EndUtc.AddMinutes(bufferMinutes);
        var hasOverlap = await context.Appointments.AnyAsync(item =>
            item.ClinicId == request.ClinicId &&
            item.ClinicalId == request.ClinicianId &&
            (request.ExcludingAppointmentId == null || item.Id != request.ExcludingAppointmentId.Value) &&
            item.Status != AppointmentStatus.Cancelled &&
            item.Status != AppointmentStatus.NoShow &&
            item.StartTimeUtc < bufferedEnd && item.EndTimeUtc > bufferedStart,
            cancellationToken);

        if (hasOverlap && preferences?.AllowDoubleBooking != true)
            reasons.Add("appointment_overlap");

        var requiresAuthorizedOverlap = hasOverlap && preferences?.AllowDoubleBooking == true;
        return new AvailabilityDecision(reasons.Count == 0, requiresAuthorizedOverlap, reasons);
    }

    private static WeekdayFlags ToFlag(DayOfWeek day) => day switch
    {
        DayOfWeek.Sunday => WeekdayFlags.Sunday,
        DayOfWeek.Monday => WeekdayFlags.Monday,
        DayOfWeek.Tuesday => WeekdayFlags.Tuesday,
        DayOfWeek.Wednesday => WeekdayFlags.Wednesday,
        DayOfWeek.Thursday => WeekdayFlags.Thursday,
        DayOfWeek.Friday => WeekdayFlags.Friday,
        DayOfWeek.Saturday => WeekdayFlags.Saturday,
        _ => WeekdayFlags.None
    };
}

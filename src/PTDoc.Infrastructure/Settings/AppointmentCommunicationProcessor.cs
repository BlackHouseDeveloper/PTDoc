using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PTDoc.Application.Communication;
using PTDoc.Application.Identity;
using PTDoc.Application.Intake;
using PTDoc.Application.Settings;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using DeliveryChannel = PTDoc.Core.Communication.DeliveryChannel;

namespace PTDoc.Infrastructure.Settings;

public sealed class AppointmentCommunicationProcessor(
    ApplicationDbContext context,
    ICommunicationService communicationService,
    IIntakeCommunicationWorkflow intakeWorkflow,
    TimeProvider timeProvider,
    ILogger<AppointmentCommunicationProcessor> logger) : IAppointmentCommunicationProcessor
{
    private const int BatchSize = 100;
    private const int MaximumAutoCheckInLeadHours = 168;

    public async Task ProcessDueAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        await RecoverInterruptedAsync(now, cancellationToken);
        await CancelObsoleteAsync(now, cancellationToken);
        await QueueEligibleAsync(now, cancellationToken);

        var dueIds = await context.AppointmentReminderDispatches
            .AsNoTracking()
            .Where(item =>
                (item.Status == ReminderDispatchStatus.Pending || item.Status == ReminderDispatchStatus.RetryScheduled)
                && item.NextAttemptAtUtc <= now)
            .OrderBy(item => item.NextAttemptAtUtc)
            .Select(item => item.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var dispatchId in dueIds)
        {
            var dispatch = await TryClaimDispatchAsync(dispatchId, now, cancellationToken);
            if (dispatch is null)
            {
                continue;
            }

            await ProcessDispatchAsync(dispatch, cancellationToken);
        }
    }

    private async Task QueueEligibleAsync(DateTime now, CancellationToken cancellationToken)
    {
        var horizon = now.AddHours(MaximumAutoCheckInLeadHours);
        var appointments = await context.Appointments
            .AsNoTracking()
            .Include(item => item.Patient)
            .Include(item => item.VisitType)
            .Where(item => item.ClinicId.HasValue
                && item.StartTimeUtc > now
                && item.StartTimeUtc <= horizon
                && item.Status != AppointmentStatus.Cancelled
                && item.Status != AppointmentStatus.Completed
                && item.Status != AppointmentStatus.NoShow)
            .Take(500)
            .ToListAsync(cancellationToken);

        if (appointments.Count == 0) return;
        var clinicIds = appointments.Select(item => item.ClinicId!.Value).Distinct().ToArray();
        var patientIds = appointments.Select(item => item.PatientId).Distinct().ToArray();
        var preferences = await context.SchedulingPreferences
            .AsNoTracking()
            .Where(item => clinicIds.Contains(item.ClinicId))
            .ToDictionaryAsync(item => item.ClinicId, cancellationToken);
        var autoPolicies = await context.AutoCheckInPolicies
            .AsNoTracking()
            .Where(item => clinicIds.Contains(item.ClinicId) && item.IsEnabled)
            .ToDictionaryAsync(item => item.ClinicId, cancellationToken);
        var intakeSummaries = await context.IntakeForms
            .AsNoTracking()
            .Where(item => patientIds.Contains(item.PatientId))
            .Select(item => new { item.PatientId, item.Consents, item.LastModifiedUtc, item.SubmittedAt })
            .ToListAsync(cancellationToken);
        var intakeConsents = intakeSummaries
            .GroupBy(item => item.PatientId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(item => item.LastModifiedUtc).First().Consents);
        var patientsWithCompletedIntake = intakeSummaries
            .Where(item => item.SubmittedAt.HasValue)
            .Select(item => item.PatientId)
            .ToHashSet();

        foreach (var appointment in appointments)
        {
            if (appointment.Patient?.ConsentSigned != true) continue;
            var communicationConsent = ResolveCommunicationConsent(
                intakeConsents.GetValueOrDefault(appointment.PatientId),
                appointment.Patient);
            var clinicId = appointment.ClinicId!.Value;
            if (preferences.TryGetValue(clinicId, out var preference)
                && preference.SendAppointmentReminders
                && IsDue(appointment.StartTimeUtc, preference.ReminderLeadHours, now))
            {
                QueueChannels(appointment, ReminderDispatchPurpose.AppointmentReminder,
                    preference.ReminderLeadHours,
                    enableEmail: communicationConsent.EmailAllowed,
                    enableSms: communicationConsent.TextAllowed,
                    now);
            }

            if (autoPolicies.TryGetValue(clinicId, out var autoPolicy)
                && appointment.VisitType?.RequiresIntake == true
                && IsDue(appointment.StartTimeUtc, autoPolicy.LeadHours, now)
                && IsEligibleVisitType(autoPolicy, appointment.VisitTypeId))
            {
                if (!patientsWithCompletedIntake.Contains(appointment.PatientId))
                {
                    QueueChannels(appointment, ReminderDispatchPurpose.AutoCheckIn,
                        autoPolicy.LeadHours,
                        autoPolicy.EnableEmail && communicationConsent.EmailAllowed,
                        autoPolicy.EnableSms && communicationConsent.TextAllowed,
                        now);
                }
            }
        }

        try { await context.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException exception)
        {
            logger.LogInformation(exception, "Duplicate appointment communication candidates were suppressed by idempotency keys.");
            context.ChangeTracker.Clear();
        }
    }

    private void QueueChannels(
        Appointment appointment,
        ReminderDispatchPurpose purpose,
        int leadHours,
        bool enableEmail,
        bool enableSms,
        DateTime now)
    {
        if (enableEmail && !string.IsNullOrWhiteSpace(appointment.Patient?.Email))
            Queue(appointment, purpose, leadHours, ReminderChannel.Email, now);
        if (enableSms && !string.IsNullOrWhiteSpace(appointment.Patient?.Phone))
            Queue(appointment, purpose, leadHours, ReminderChannel.Sms, now);
    }

    private void Queue(
        Appointment appointment,
        ReminderDispatchPurpose purpose,
        int leadHours,
        ReminderChannel channel,
        DateTime now)
    {
        var version = appointment.LastModifiedUtc == default ? appointment.StartTimeUtc : appointment.LastModifiedUtc;
        var key = $"{purpose}:{appointment.Id:N}:{version.Ticks}:{leadHours}:{channel}";
        if (context.AppointmentReminderDispatches.Local.Any(item => item.IdempotencyKey == key)) return;
        context.AppointmentReminderDispatches.Add(new AppointmentReminderDispatch
        {
            ClinicId = appointment.ClinicId!.Value,
            AppointmentId = appointment.Id,
            AppointmentVersionUtc = version,
            ReminderLeadHours = leadHours,
            Purpose = purpose,
            Channel = channel,
            IdempotencyKey = key,
            Status = ReminderDispatchStatus.Pending,
            EligibleAtUtc = appointment.StartTimeUtc.AddHours(-leadHours),
            NextAttemptAtUtc = now,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
    }

    private async Task ProcessDispatchAsync(AppointmentReminderDispatch dispatch, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var appointment = await context.Appointments
            .AsNoTracking()
            .Include(item => item.Patient)
            .Include(item => item.Clinic)
            .Include(item => item.VisitType)
            .SingleOrDefaultAsync(item => item.Id == dispatch.AppointmentId && item.ClinicId == dispatch.ClinicId, cancellationToken);
        if (appointment?.Patient is null
            || appointment.Status is AppointmentStatus.Cancelled or AppointmentStatus.Completed or AppointmentStatus.NoShow
            || appointment.StartTimeUtc <= now)
        {
            Suppress(dispatch, "appointment_ineligible", now);
            await context.SaveChangesAsync(cancellationToken);
            return;
        }

        var latestConsentJson = await context.IntakeForms
            .AsNoTracking()
            .Where(item => item.PatientId == appointment.PatientId)
            .OrderByDescending(item => item.LastModifiedUtc)
            .Select(item => item.Consents)
            .FirstOrDefaultAsync(cancellationToken);
        var communicationConsent = ResolveCommunicationConsent(latestConsentJson, appointment.Patient);
        var channelAllowed = dispatch.Channel == ReminderChannel.Email
            ? communicationConsent.EmailAllowed
            : communicationConsent.TextAllowed;
        if (appointment.Patient.ConsentSigned != true || !channelAllowed)
        {
            Suppress(dispatch, "communication_consent_unavailable", now);
            await context.SaveChangesAsync(cancellationToken);
            return;
        }

        try
        {
            var result = dispatch.Purpose == ReminderDispatchPurpose.AutoCheckIn
                ? await SendAutoCheckInAsync(dispatch, appointment, cancellationToken)
                : await SendReminderAsync(dispatch, appointment, cancellationToken);
            if (result.Success)
            {
                dispatch.Status = ReminderDispatchStatus.Sent;
                dispatch.CompletedAtUtc = now;
                dispatch.LastStatusCode = result.StatusCode;
            }
            else
            {
                ScheduleRetry(dispatch, result.StatusCode, now,
                    dispatch.Purpose == ReminderDispatchPurpose.AutoCheckIn ? await GetAutoMaxAttemptsAsync(dispatch.ClinicId, cancellationToken) : 5);
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception,
                "Appointment communication failed. DispatchId={DispatchId} AppointmentId={AppointmentId} ClinicId={ClinicId} Purpose={Purpose} Channel={Channel}",
                dispatch.Id, dispatch.AppointmentId, dispatch.ClinicId, dispatch.Purpose, dispatch.Channel);
            ScheduleRetry(dispatch, "delivery_exception", now, 5);
        }

        dispatch.UpdatedAtUtc = now;
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task<DeliveryOutcome> SendReminderAsync(
        AppointmentReminderDispatch dispatch,
        Appointment appointment,
        CancellationToken cancellationToken)
    {
        var zone = ResolveTimeZone(appointment.Clinic?.TimeZoneId);
        var local = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(appointment.StartTimeUtc, DateTimeKind.Utc), zone);
        var zoneName = zone.IsDaylightSavingTime(local)
            ? zone.DaylightName
            : zone.StandardName;
        var request = new AppointmentReminderDeliveryRequest
        {
            AppointmentId = appointment.Id,
            PatientId = appointment.PatientId,
            ClinicId = dispatch.ClinicId,
            Recipient = dispatch.Channel == ReminderChannel.Email ? appointment.Patient!.Email! : appointment.Patient!.Phone!,
            AppointmentLocalTime = $"{local:ddd, MMM d 'at' h:mm tt} {zoneName}",
            CorrelationId = dispatch.Id.ToString("N")
        };
        var result = dispatch.Channel == ReminderChannel.Email
            ? await communicationService.SendAppointmentReminderEmailAsync(request, cancellationToken)
            : await communicationService.SendAppointmentReminderSmsAsync(request, cancellationToken);
        return new DeliveryOutcome(result.Succeeded, result.ErrorCode ?? result.Status.ToString());
    }

    private async Task<DeliveryOutcome> SendAutoCheckInAsync(
        AppointmentReminderDispatch dispatch,
        Appointment appointment,
        CancellationToken cancellationToken)
    {
        var intake = await context.IntakeForms
            .Where(item => item.PatientId == appointment.PatientId && !item.IsLocked && !item.SubmittedAt.HasValue)
            .OrderByDescending(item => item.LastModifiedUtc)
            .FirstOrDefaultAsync(cancellationToken);
        if (intake is null)
        {
            intake = new IntakeForm
            {
                PatientId = appointment.PatientId,
                ClinicId = dispatch.ClinicId,
                TemplateVersion = "1.0",
                AccessToken = PlaceholderHash(),
                ResponseJson = "{}",
                PainMapData = "{}",
                Consents = "{}",
                LastModifiedUtc = timeProvider.GetUtcNow().UtcDateTime,
                ModifiedByUserId = IIdentityContextAccessor.SystemUserId,
                SyncState = SyncState.Pending
            };
            context.IntakeForms.Add(intake);
            await context.SaveChangesAsync(cancellationToken);
        }

        var result = await intakeWorkflow.SendInviteAsync(new IntakeSendInviteRequest
        {
            IntakeId = intake.Id,
            Channel = dispatch.Channel == ReminderChannel.Email
                ? IntakeDeliveryChannel.Email
                : IntakeDeliveryChannel.Sms
        }, new IntakeCommunicationContext
        {
            UserId = IIdentityContextAccessor.SystemUserId,
            CorrelationId = dispatch.Id.ToString("N")
        }, cancellationToken);
        return new DeliveryOutcome(result.Success, result.Success ? "Sent" : "auto_check_in_delivery_failed");
    }

    private async Task<int> GetAutoMaxAttemptsAsync(Guid clinicId, CancellationToken cancellationToken) =>
        await context.AutoCheckInPolicies.Where(item => item.ClinicId == clinicId)
            .Select(item => (int?)item.MaxAttempts).SingleOrDefaultAsync(cancellationToken) ?? 3;

    private async Task<AppointmentReminderDispatch?> TryClaimDispatchAsync(
        Guid dispatchId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (!context.Database.IsRelational())
        {
            var inMemoryDispatch = await context.AppointmentReminderDispatches
                .SingleOrDefaultAsync(item => item.Id == dispatchId, cancellationToken);
            if (inMemoryDispatch is null
                || (inMemoryDispatch.Status != ReminderDispatchStatus.Pending
                    && inMemoryDispatch.Status != ReminderDispatchStatus.RetryScheduled)
                || inMemoryDispatch.NextAttemptAtUtc > now)
            {
                return null;
            }

            inMemoryDispatch.Status = ReminderDispatchStatus.Processing;
            inMemoryDispatch.AttemptCount++;
            inMemoryDispatch.UpdatedAtUtc = now;
            await context.SaveChangesAsync(cancellationToken);
            return inMemoryDispatch;
        }

        var claimed = await context.AppointmentReminderDispatches
            .Where(item => item.Id == dispatchId
                && (item.Status == ReminderDispatchStatus.Pending
                    || item.Status == ReminderDispatchStatus.RetryScheduled)
                && item.NextAttemptAtUtc <= now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, ReminderDispatchStatus.Processing)
                .SetProperty(item => item.AttemptCount, item => item.AttemptCount + 1)
                .SetProperty(item => item.UpdatedAtUtc, now), cancellationToken);
        if (claimed == 0)
        {
            return null;
        }

        var trackedEntry = context.ChangeTracker.Entries<AppointmentReminderDispatch>()
            .SingleOrDefault(entry => entry.Entity.Id == dispatchId);
        if (trackedEntry is not null)
        {
            await trackedEntry.ReloadAsync(cancellationToken);
            return trackedEntry.Entity;
        }

        return await context.AppointmentReminderDispatches
            .SingleAsync(item => item.Id == dispatchId, cancellationToken);
    }

    private async Task RecoverInterruptedAsync(DateTime now, CancellationToken cancellationToken)
    {
        var stale = await context.AppointmentReminderDispatches
            .Where(item => item.Status == ReminderDispatchStatus.Processing && item.UpdatedAtUtc < now.AddMinutes(-10))
            .ToListAsync(cancellationToken);
        foreach (var item in stale)
        {
            item.Status = ReminderDispatchStatus.RetryScheduled;
            item.NextAttemptAtUtc = now;
            item.LastStatusCode = "interrupted";
            item.UpdatedAtUtc = now;
        }
        if (stale.Count > 0) await context.SaveChangesAsync(cancellationToken);
    }

    private async Task CancelObsoleteAsync(DateTime now, CancellationToken cancellationToken)
    {
        var candidates = await context.AppointmentReminderDispatches
            .Include(item => item.Appointment)
            .Where(item => item.Status == ReminderDispatchStatus.Pending || item.Status == ReminderDispatchStatus.RetryScheduled)
            .ToListAsync(cancellationToken);
        foreach (var item in candidates)
        {
            var appointment = item.Appointment;
            var version = appointment?.LastModifiedUtc == default ? appointment?.StartTimeUtc : appointment?.LastModifiedUtc;
            if (appointment is null
                || appointment.Status is AppointmentStatus.Cancelled or AppointmentStatus.Completed or AppointmentStatus.NoShow
                || version != item.AppointmentVersionUtc)
            {
                item.Status = ReminderDispatchStatus.Cancelled;
                item.LastStatusCode = "appointment_changed";
                item.UpdatedAtUtc = now;
            }
        }
        if (context.ChangeTracker.HasChanges()) await context.SaveChangesAsync(cancellationToken);
    }

    private static void ScheduleRetry(AppointmentReminderDispatch dispatch, string code, DateTime now, int maxAttempts)
    {
        dispatch.LastStatusCode = Truncate(code, 80);
        if (dispatch.AttemptCount >= Math.Clamp(maxAttempts, 1, 10))
        {
            dispatch.Status = ReminderDispatchStatus.DeadLetter;
            dispatch.CompletedAtUtc = now;
            return;
        }

        dispatch.Status = ReminderDispatchStatus.RetryScheduled;
        dispatch.NextAttemptAtUtc = now.AddMinutes(Math.Min(60, 5 * Math.Pow(2, dispatch.AttemptCount - 1)));
    }

    private static void Suppress(AppointmentReminderDispatch dispatch, string reasonCode, DateTime now)
    {
        dispatch.Status = ReminderDispatchStatus.Suppressed;
        dispatch.LastStatusCode = reasonCode;
        dispatch.CompletedAtUtc = now;
        dispatch.UpdatedAtUtc = now;
    }

    private static bool IsDue(DateTime appointmentUtc, int leadHours, DateTime now) =>
        appointmentUtc.AddHours(-leadHours) <= now && appointmentUtc > now;

    private static bool IsEligibleVisitType(AutoCheckInPolicy policy, Guid? visitTypeId)
    {
        if (!visitTypeId.HasValue) return false;
        try
        {
            var eligible = JsonSerializer.Deserialize<Guid[]>(policy.EligibleVisitTypeIdsJson) ?? [];
            return eligible.Contains(visitTypeId.Value);
        }
        catch (JsonException) { return false; }
    }

    private static CommunicationConsent ResolveCommunicationConsent(string? consentJson, Patient patient)
    {
        if (!IntakeConsentJson.TryParse(consentJson, out var packet, out _))
            return new CommunicationConsent(false, false);

        var emailAllowed = IntakeConsentJson.IsEmailConsentActive(packet)
            && string.Equals(packet.CommunicationEmail?.Trim(), patient.Email?.Trim(), StringComparison.OrdinalIgnoreCase);
        var textAllowed = IntakeConsentJson.IsTextConsentActive(packet)
            && NormalizePhone(packet.CommunicationPhoneNumber) is { Length: > 0 } consentPhone
            && string.Equals(consentPhone, NormalizePhone(patient.Phone), StringComparison.Ordinal);
        return new CommunicationConsent(emailAllowed, textAllowed);
    }

    private static string NormalizePhone(string? value) =>
        new((value ?? string.Empty).Where(char.IsDigit).ToArray());

    private static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId ?? "America/Los_Angeles"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.Utc; }
        catch (InvalidTimeZoneException) { return TimeZoneInfo.Utc; }
    }

    private static string PlaceholderHash() =>
        Convert.ToHexString(SHA256.HashData(RandomNumberGenerator.GetBytes(32))).ToLowerInvariant();

    private static string Truncate(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength];

    private sealed record DeliveryOutcome(bool Success, string StatusCode);
    private sealed record CommunicationConsent(bool EmailAllowed, bool TextAllowed);
}

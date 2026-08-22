using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Settings;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Infrastructure.Settings;

public sealed class AppointmentCheckInWorkflow(
    ApplicationDbContext context,
    TimeProvider timeProvider) : IAppointmentCheckInWorkflow
{
    private static readonly CultureInfo CopayCulture = CultureInfo.GetCultureInfo("en-US");

    public async Task<AppointmentCheckInDecision> CheckInAsync(
        Guid appointmentId,
        Guid? requiredClinicId = null,
        CancellationToken cancellationToken = default)
    {
        var query = requiredClinicId.HasValue
            ? context.Appointments.IgnoreQueryFilters()
            : context.Appointments;
        var appointment = await query
            .Include(item => item.Patient)
            .SingleOrDefaultAsync(item => item.Id == appointmentId
                && (!requiredClinicId.HasValue || item.ClinicId == requiredClinicId), cancellationToken);
        if (appointment?.Patient is null) return new AppointmentCheckInDecision(AppointmentCheckInStatus.NotFound);
        if (appointment.Status is AppointmentStatus.Cancelled or AppointmentStatus.NoShow)
            return new AppointmentCheckInDecision(AppointmentCheckInStatus.Ineligible);

        var hasPaid = await context.AppointmentPaymentTransactions
            .IgnoreQueryFilters()
            .AnyAsync(item => item.AppointmentId == appointment.Id
                && item.Status == AppointmentPaymentStatus.Succeeded, cancellationToken);
        if (!hasPaid && TryParseCopay(appointment.Patient.PayerInfoJson) is > 0)
            return new AppointmentCheckInDecision(AppointmentCheckInStatus.PaymentRequired);

        var checkedInAt = timeProvider.GetUtcNow().UtcDateTime;
        if (appointment.Status is not (AppointmentStatus.CheckedIn or AppointmentStatus.InProgress or AppointmentStatus.Completed))
        {
            appointment.Status = AppointmentStatus.CheckedIn;
            appointment.LastModifiedUtc = checkedInAt;
            await context.SaveChangesAsync(cancellationToken);
        }

        return new AppointmentCheckInDecision(AppointmentCheckInStatus.Succeeded, checkedInAt);
    }

    private static decimal? TryParseCopay(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}") return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("copayAmount", out var value)) return null;
            return value.ValueKind switch
            {
                JsonValueKind.Number when value.TryGetDecimal(out var amount) => amount,
                JsonValueKind.String when decimal.TryParse(value.GetString()?.Trim(),
                    NumberStyles.Number | NumberStyles.AllowCurrencySymbol, CopayCulture, out var amount) => amount,
                _ => null
            };
        }
        catch (JsonException) { return null; }
    }
}

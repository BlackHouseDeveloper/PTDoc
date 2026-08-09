using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Appointments;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Infrastructure.Services;

/// <summary>
/// Allocates patient-scoped visit ordinals. The database unique index protects
/// against concurrent schedulers selecting the same value.
/// </summary>
public sealed class ClinicalVisitOrdinalAllocator(ApplicationDbContext db) : IClinicalVisitOrdinalAllocator
{
    public async Task<int> GetNextAsync(Guid patientId, CancellationToken cancellationToken = default)
    {
        if (patientId == Guid.Empty)
        {
            throw new ArgumentException("A patient is required to allocate a clinical visit ordinal.", nameof(patientId));
        }

        var patientAppointments = db.Appointments
            .AsNoTracking()
            .Where(appointment => appointment.PatientId == patientId);
        var highestOrdinal = await patientAppointments
            .Where(appointment => appointment.ClinicalVisitOrdinal != null)
            .MaxAsync(appointment => appointment.ClinicalVisitOrdinal, cancellationToken)
            ?? 0;
        var eligibleLegacyCount = await patientAppointments.CountAsync(
            appointment => appointment.Status == PTDoc.Core.Models.AppointmentStatus.Scheduled
                || appointment.Status == PTDoc.Core.Models.AppointmentStatus.Confirmed
                || appointment.Status == PTDoc.Core.Models.AppointmentStatus.CheckedIn
                || appointment.Status == PTDoc.Core.Models.AppointmentStatus.InProgress
                || appointment.Status == PTDoc.Core.Models.AppointmentStatus.Completed,
            cancellationToken);

        return checked(Math.Max(highestOrdinal, eligibleLegacyCount) + 1);
    }
}

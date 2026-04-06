using PTDoc.Application.DTOs;

namespace PTDoc.Application.Services;

/// <summary>
/// Contract for appointment scheduling reads and basic write actions used by the appointments page.
/// </summary>
public interface IAppointmentService
{
    /// <summary>
    /// Returns appointments and clinician options for the requested date window.
    /// </summary>
    Task<AppointmentsOverviewResponse> GetOverviewAsync(
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns appointment rows for a single patient within the requested date window.
    /// </summary>
    Task<IReadOnlyList<AppointmentListItemResponse>> GetByPatientAsync(
        Guid patientId,
        DateTime startDate,
        DateTime endDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns active clinicians available for scheduling/export filters.
    /// </summary>
    Task<IReadOnlyList<AppointmentClinicianResponse>> GetCliniciansAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new appointment and returns the resulting scheduling projection.
    /// </summary>
    Task<AppointmentListItemResponse> CreateAsync(
        CreateAppointmentRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing appointment and returns the resulting scheduling projection, or null if not found.
    /// </summary>
    Task<AppointmentListItemResponse?> UpdateAsync(
        Guid id,
        UpdateAppointmentRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an appointment as checked in and returns the updated scheduling projection, or null if not found.
    /// </summary>
    Task<AppointmentListItemResponse?> CheckInAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}

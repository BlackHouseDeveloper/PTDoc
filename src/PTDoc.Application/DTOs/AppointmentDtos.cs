namespace PTDoc.Application.DTOs;

/// <summary>
/// Request DTO for creating a new appointment.
/// Appointment date and time are interpreted in the practice's local time zone.
/// </summary>
public sealed class CreateAppointmentRequest
{
    public Guid PatientId { get; set; }
    public Guid ClinicianId { get; set; }
    public string AppointmentType { get; set; } = string.Empty;
    public DateTime AppointmentDate { get; set; }
    public TimeSpan AppointmentTime { get; set; }
    public int DurationMinutes { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// Request DTO for updating an existing appointment.
/// Appointment date and time are interpreted in the practice's local time zone.
/// </summary>
public sealed class UpdateAppointmentRequest
{
    public Guid PatientId { get; set; }
    public Guid ClinicianId { get; set; }
    public string AppointmentType { get; set; } = string.Empty;
    public DateTime AppointmentDate { get; set; }
    public TimeSpan AppointmentTime { get; set; }
    public int DurationMinutes { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// Lightweight appointment projection for scheduling UIs.
/// Times are returned in UTC and converted to local display time by the client.
/// </summary>
public sealed class AppointmentListItemResponse
{
    public Guid Id { get; set; }
    public Guid PatientRecordId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string? MedicalRecordNumber { get; set; }
    public Guid? ClinicianId { get; set; }
    public string ClinicianName { get; set; } = string.Empty;
    public DateTime StartTimeUtc { get; set; }
    public DateTime EndTimeUtc { get; set; }
    public string AppointmentType { get; set; } = string.Empty;
    public string AppointmentStatus { get; set; } = string.Empty;
    public string VisitWorkflowStatus { get; set; } = string.Empty;
    public Guid? VisitNoteId { get; set; }
    public string IntakeStatus { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public decimal? CopayAmount { get; set; }
    public string CopayStatusLabel { get; set; } = "Copay not configured";
    public bool CanRecordCopay { get; set; }
    public string CopayActionUnavailableReason { get; set; } = "Copay collection is not configured for this appointment.";
    public string? SuccessfulPaymentTransactionId { get; set; }
}

public sealed class AppointmentCheckInPaymentRequest
{
    public string OpaqueDataDescriptor { get; set; } = string.Empty;
    public string OpaqueDataToken { get; set; } = string.Empty;
    public bool CheckInAfterPayment { get; set; } = true;
    public string? BillingFirstName { get; set; }
    public string? BillingLastName { get; set; }
    public string? BillingZip { get; set; }
}

public sealed class AppointmentCheckInPaymentResponse
{
    public AppointmentListItemResponse? Appointment { get; set; }
    public PTDoc.Application.Integrations.PaymentResult Payment { get; set; } = new();
}

/// <summary>
/// Lightweight clinician projection for scheduling and appointment-form UIs.
/// </summary>
public sealed class AppointmentClinicianResponse
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
}

/// <summary>
/// Composite scheduling payload containing both appointments and selectable clinicians.
/// </summary>
public sealed class AppointmentsOverviewResponse
{
    public IReadOnlyList<AppointmentListItemResponse> Appointments { get; set; } = Array.Empty<AppointmentListItemResponse>();
    public IReadOnlyList<AppointmentClinicianResponse> Clinicians { get; set; } = Array.Empty<AppointmentClinicianResponse>();
}

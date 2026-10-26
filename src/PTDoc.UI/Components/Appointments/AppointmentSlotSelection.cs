namespace PTDoc.UI.Components.Appointments;

/// <summary>A clinician and wall-clock slot in the clinic's configured time zone.</summary>
public sealed record AppointmentSlotSelection(Guid ClinicianId, DateTime Date, TimeSpan Time);

namespace PTDoc.Core.Models;

public class AppointmentPaymentTransaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AppointmentId { get; set; }
    public Guid PatientId { get; set; }
    public decimal Amount { get; set; }
    public AppointmentPaymentStatus Status { get; set; } = AppointmentPaymentStatus.Pending;
    public string Processor { get; set; } = "AuthorizeNet";
    public string? TransactionId { get; set; }
    public string? AuthorizationCode { get; set; }
    public string? GatewayErrorCode { get; set; }
    public string? GatewayErrorMessage { get; set; }
    public string? InvoiceNumber { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAtUtc { get; set; }

    public Appointment? Appointment { get; set; }
    public Patient? Patient { get; set; }
}

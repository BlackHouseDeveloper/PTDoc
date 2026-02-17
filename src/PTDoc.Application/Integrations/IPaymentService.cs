namespace PTDoc.Application.Integrations;

/// <summary>
/// Payment processing service interface for accepting patient payments.
/// Implementation lives in PTDoc.Integrations project.
/// </summary>
public interface IPaymentService
{
    /// <summary>
    /// Process a payment using tokenized payment data (e.g., Authorize.Net Accept.js).
    /// </summary>
    Task<PaymentResult> ProcessPaymentAsync(PaymentRequest request, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Refund a previously processed payment.
    /// </summary>
    Task<PaymentResult> RefundPaymentAsync(string transactionId, decimal amount, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get payment transaction details.
    /// </summary>
    Task<PaymentResult> GetTransactionDetailsAsync(string transactionId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Request for processing a payment.
/// Uses tokenized payment data (no PCI-sensitive information).
/// </summary>
public class PaymentRequest
{
    /// <summary>
    /// Opaque data token from Authorize.Net Accept.js or similar tokenization service.
    /// Contains encrypted payment information.
    /// </summary>
    public string OpaqueDataToken { get; set; } = string.Empty;
    
    /// <summary>
    /// Data descriptor from tokenization service.
    /// </summary>
    public string OpaqueDataDescriptor { get; set; } = string.Empty;
    
    /// <summary>
    /// Amount to charge (in dollars).
    /// </summary>
    public decimal Amount { get; set; }
    
    /// <summary>
    /// Internal patient ID for mapping and audit trail.
    /// </summary>
    public Guid PatientId { get; set; }
    
    /// <summary>
    /// Optional description for the transaction.
    /// </summary>
    public string? Description { get; set; }
    
    /// <summary>
    /// Invoice or reference number.
    /// </summary>
    public string? InvoiceNumber { get; set; }
}

/// <summary>
/// Result of a payment operation.
/// </summary>
public class PaymentResult
{
    public bool Success { get; set; }
    public string? TransactionId { get; set; }
    public string? AuthorizationCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ErrorCode { get; set; }
    public DateTime ProcessedAt { get; set; }
    public decimal? Amount { get; set; }
}

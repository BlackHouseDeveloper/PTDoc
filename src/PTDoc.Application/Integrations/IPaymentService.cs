namespace PTDoc.Application.Integrations;

/// <summary>
/// Interface for payment processing (Authorize.Net).
/// Server-side only - uses tokenized payment data from client.
/// </summary>
public interface IPaymentService
{
    /// <summary>
    /// Creates a payment transaction using a tokenized payment method.
    /// </summary>
    /// <param name="request">Payment request with opaque data token from Accept.js</param>
    Task<PaymentResult> ProcessPaymentAsync(PaymentRequest request, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Retrieves transaction details by transaction ID.
    /// </summary>
    Task<PaymentTransactionDetails?> GetTransactionDetailsAsync(string transactionId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Refunds a previously processed transaction.
    /// </summary>
    Task<PaymentResult> RefundTransactionAsync(string transactionId, decimal amount, CancellationToken cancellationToken = default);
}

/// <summary>
/// Payment request with tokenized payment data.
/// </summary>
public class PaymentRequest
{
    public Guid PatientId { get; set; }
    public decimal Amount { get; set; }
    public string Description { get; set; } = string.Empty;
    
    /// <summary>
    /// Opaque data token from Accept.js (client-side tokenization).
    /// </summary>
    public string OpaqueDataToken { get; set; } = string.Empty;
    
    /// <summary>
    /// Opaque data descriptor from Accept.js.
    /// </summary>
    public string OpaqueDataDescriptor { get; set; } = string.Empty;
    
    public string? InvoiceNumber { get; set; }
    public Guid ProcessedByUserId { get; set; }
}

/// <summary>
/// Result of a payment transaction.
/// </summary>
public class PaymentResult
{
    public bool IsSuccessful { get; set; }
    public string? TransactionId { get; set; }
    public string? ErrorMessage { get; set; }
    public string? AuthorizationCode { get; set; }
    public DateTime ProcessedUtc { get; set; }
}

/// <summary>
/// Payment transaction details.
/// </summary>
public class PaymentTransactionDetails
{
    public string TransactionId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime ProcessedUtc { get; set; }
    public string? Last4Digits { get; set; }
    public string? CardType { get; set; }
}

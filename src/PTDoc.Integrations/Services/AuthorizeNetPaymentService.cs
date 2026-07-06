using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using PTDoc.Application.Integrations;

namespace PTDoc.Integrations.Services;

/// <summary>
/// Authorize.Net payment service implementation.
/// Uses Accept.js for PCI-compliant tokenized payments.
/// </summary>
public class AuthorizeNetPaymentService : IPaymentService
{
    private const string SandboxGatewayUrl = "https://apitest.authorize.net/xml/v1/request.api";
    private const string ProductionGatewayUrl = "https://api.authorize.net/xml/v1/request.api";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string? _apiLoginId;
    private readonly string? _transactionKey;
    private readonly string? _clientKey;
    private readonly string _gatewayUrl;
    private readonly bool _isEnabled;

    public AuthorizeNetPaymentService(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _apiLoginId = configuration["Integrations:Payments:ApiLoginId"];
        _transactionKey = configuration["Integrations:Payments:TransactionKey"];
        _clientKey = configuration["Integrations:Payments:ClientKey"];
        _isEnabled = configuration.GetValue<bool>("Integrations:Payments:Enabled");
        _gatewayUrl = ResolveGatewayUrl(configuration["Integrations:Payments:Environment"]);
    }

    public async Task<PaymentResult> ProcessPaymentAsync(PaymentRequest request, CancellationToken cancellationToken = default)
    {
        if (!_isEnabled)
        {
            return new PaymentResult
            {
                Success = false,
                ErrorMessage = "Payment processing is disabled",
                ErrorCode = "DISABLED",
                ProcessedAt = DateTime.UtcNow
            };
        }

        if (string.IsNullOrWhiteSpace(_apiLoginId)
            || string.IsNullOrWhiteSpace(_transactionKey)
            || string.IsNullOrWhiteSpace(_clientKey))
        {
            return new PaymentResult
            {
                Success = false,
                ErrorMessage = "Payment service not configured",
                ErrorCode = "NOT_CONFIGURED",
                ProcessedAt = DateTime.UtcNow
            };
        }

        if (string.IsNullOrWhiteSpace(request.OpaqueDataDescriptor) || string.IsNullOrWhiteSpace(request.OpaqueDataToken))
        {
            return new PaymentResult
            {
                Success = false,
                ErrorMessage = "Payment token is required",
                ErrorCode = "PAYMENT_TOKEN_REQUIRED",
                ProcessedAt = DateTime.UtcNow,
                Amount = request.Amount
            };
        }

        if (request.Amount <= 0)
        {
            return new PaymentResult
            {
                Success = false,
                ErrorMessage = "Payment amount must be greater than zero",
                ErrorCode = "INVALID_AMOUNT",
                ProcessedAt = DateTime.UtcNow,
                Amount = request.Amount
            };
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            using var response = await client.PostAsJsonAsync(_gatewayUrl, BuildCreateTransactionPayload(request), JsonOptions, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new PaymentResult
                {
                    Success = false,
                    ErrorMessage = "Payment gateway request failed",
                    ErrorCode = response.StatusCode.ToString(),
                    ProcessedAt = DateTime.UtcNow,
                    Amount = request.Amount
                };
            }

            return ParsePaymentResult(content, request.Amount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new PaymentResult
            {
                Success = false,
                ErrorMessage = "Payment gateway request failed",
                ErrorCode = "GATEWAY_REQUEST_FAILED",
                ProcessedAt = DateTime.UtcNow,
                Amount = request.Amount
            };
        }
    }

    public async Task<PaymentResult> RefundPaymentAsync(string transactionId, decimal amount, CancellationToken cancellationToken = default)
    {
        if (!_isEnabled)
        {
            return new PaymentResult
            {
                Success = false,
                ErrorMessage = "Payment processing is disabled",
                ErrorCode = "DISABLED",
                ProcessedAt = DateTime.UtcNow
            };
        }

        // Stub refund path for environments without live payment gateway wiring.
        await Task.Delay(100, cancellationToken);

        return new PaymentResult
        {
            Success = true,
            TransactionId = $"MOCK-REFUND-{Guid.NewGuid():N}",
            ProcessedAt = DateTime.UtcNow,
            Amount = amount
        };
    }

    public async Task<PaymentResult> GetTransactionDetailsAsync(string transactionId, CancellationToken cancellationToken = default)
    {
        if (!_isEnabled)
        {
            return new PaymentResult
            {
                Success = false,
                ErrorMessage = "Payment processing is disabled",
                ErrorCode = "DISABLED",
                ProcessedAt = DateTime.UtcNow
            };
        }

        // Stub lookup path for environments without live payment gateway wiring.
        await Task.Delay(50, cancellationToken);

        return new PaymentResult
        {
            Success = true,
            TransactionId = transactionId,
            ProcessedAt = DateTime.UtcNow
        };
    }

    private object BuildCreateTransactionPayload(PaymentRequest request)
    {
        var appointmentReference = request.AppointmentId?.ToString("N") ?? Guid.NewGuid().ToString("N");
        var trimmedInvoiceNumber = request.InvoiceNumber?.Trim();
        var invoiceNumber = string.IsNullOrWhiteSpace(trimmedInvoiceNumber)
            ? appointmentReference[..Math.Min(20, appointmentReference.Length)]
            : trimmedInvoiceNumber[..Math.Min(20, trimmedInvoiceNumber.Length)];

        return new
        {
            createTransactionRequest = new
            {
                merchantAuthentication = new
                {
                    name = _apiLoginId,
                    transactionKey = _transactionKey
                },
                refId = invoiceNumber,
                transactionRequest = new
                {
                    transactionType = "authCaptureTransaction",
                    amount = request.Amount.ToString("0.00", CultureInfo.InvariantCulture),
                    payment = new
                    {
                        opaqueData = new
                        {
                            dataDescriptor = request.OpaqueDataDescriptor,
                            dataValue = request.OpaqueDataToken
                        }
                    },
                    order = new
                    {
                        invoiceNumber,
                        description = request.Description
                    },
                    customer = new
                    {
                        id = request.PatientId.ToString("N")
                    }
                }
            }
        };
    }

    private static PaymentResult ParsePaymentResult(string content, decimal amount)
    {
        try
        {
            using var document = JsonDocument.Parse(content.TrimStart('\uFEFF'));
            var root = document.RootElement;
            var messages = root.TryGetProperty("messages", out var messagesElement) ? messagesElement : default;
            if (!root.TryGetProperty("transactionResponse", out var transactionResponse))
            {
                var (gatewayCode, gatewayMessage) = GetMessage(messages);
                return new PaymentResult
                {
                    Success = false,
                    ErrorCode = string.IsNullOrWhiteSpace(gatewayCode) ? "GATEWAY_VALIDATION_ERROR" : gatewayCode,
                    ErrorMessage = string.IsNullOrWhiteSpace(gatewayMessage)
                        ? "Payment gateway rejected the transaction request."
                        : gatewayMessage,
                    ProcessedAt = DateTime.UtcNow,
                    Amount = amount
                };
            }

            var responseCode = transactionResponse.TryGetProperty("responseCode", out var responseCodeElement)
                ? responseCodeElement.GetString()
                : null;
            var success = string.Equals(responseCode, "1", StringComparison.OrdinalIgnoreCase)
                && string.Equals(GetResultCode(messages), "Ok", StringComparison.OrdinalIgnoreCase);

            if (success)
            {
                return new PaymentResult
                {
                    Success = true,
                    TransactionId = GetOptionalString(transactionResponse, "transId"),
                    AuthorizationCode = GetOptionalString(transactionResponse, "authCode"),
                    ProcessedAt = DateTime.UtcNow,
                    Amount = amount
                };
            }

            var (code, message) = GetTransactionError(transactionResponse);
            if (string.IsNullOrWhiteSpace(message))
            {
                (code, message) = GetMessage(messages);
            }

            return new PaymentResult
            {
                Success = false,
                ErrorCode = string.IsNullOrWhiteSpace(code) ? "PAYMENT_DECLINED" : code,
                ErrorMessage = string.IsNullOrWhiteSpace(message) ? "Payment was declined." : message,
                TransactionId = GetOptionalString(transactionResponse, "transId"),
                ProcessedAt = DateTime.UtcNow,
                Amount = amount
            };
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return new PaymentResult
            {
                Success = false,
                ErrorCode = "GATEWAY_RESPONSE_INVALID",
                ErrorMessage = "Payment gateway returned an invalid response",
                ProcessedAt = DateTime.UtcNow,
                Amount = amount
            };
        }
    }

    private static string ResolveGatewayUrl(string? environment) =>
        string.Equals(environment, "Production", StringComparison.OrdinalIgnoreCase)
            ? ProductionGatewayUrl
            : SandboxGatewayUrl;

    private static string? GetOptionalString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind != JsonValueKind.Null
            ? property.GetString()
            : null;

    private static string? GetResultCode(JsonElement messages) =>
        messages.ValueKind == JsonValueKind.Object && messages.TryGetProperty("resultCode", out var resultCode)
            ? resultCode.GetString()
            : null;

    private static (string? Code, string? Message) GetMessage(JsonElement messages)
    {
        if (messages.ValueKind != JsonValueKind.Object
            || !messages.TryGetProperty("message", out var messageArray)
            || messageArray.ValueKind != JsonValueKind.Array)
        {
            return (null, null);
        }

        var first = messageArray.EnumerateArray().FirstOrDefault();
        return first.ValueKind == JsonValueKind.Object
            ? (GetOptionalString(first, "code"), GetOptionalString(first, "text"))
            : (null, null);
    }

    private static (string? Code, string? Message) GetTransactionError(JsonElement transactionResponse)
    {
        if (!transactionResponse.TryGetProperty("errors", out var errors))
        {
            return (null, null);
        }

        JsonElement first;
        if (errors.ValueKind == JsonValueKind.Array)
        {
            first = errors.EnumerateArray().FirstOrDefault();
        }
        else if (errors.ValueKind == JsonValueKind.Object
            && errors.TryGetProperty("error", out var errorArray)
            && errorArray.ValueKind == JsonValueKind.Array)
        {
            first = errorArray.EnumerateArray().FirstOrDefault();
        }
        else
        {
            return (null, null);
        }

        return first.ValueKind == JsonValueKind.Object
            ? (GetOptionalString(first, "errorCode"), GetOptionalString(first, "errorText"))
            : (null, null);
    }
}

using Microsoft.Extensions.Configuration;
using PTDoc.Application.Integrations;

namespace PTDoc.Integrations.Services;

/// <summary>
/// HumbleFax service implementation for sending faxes.
/// </summary>
public class HumbleFaxService : IFaxService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string? _apiKey;
    private readonly bool _isEnabled;

    public HumbleFaxService(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _apiKey = configuration["Integrations:Fax:ApiKey"];
        _isEnabled = configuration.GetValue<bool>("Integrations:Fax:Enabled");
    }

    public async Task<FaxResult> SendFaxAsync(FaxRequest request, CancellationToken cancellationToken = default)
    {
        if (!_isEnabled)
        {
            return new FaxResult
            {
                Success = false,
                ErrorMessage = "Fax service is disabled"
            };
        }

        if (string.IsNullOrEmpty(_apiKey))
        {
            return new FaxResult
            {
                Success = false,
                ErrorMessage = "Fax service not configured"
            };
        }

        // TODO: Implement actual HumbleFax API integration
        // This is a mock implementation for now
        // Production would call HumbleFax API with PDF content

        // Mock successful fax for development
        await Task.Delay(200, cancellationToken); // Simulate API call

        return new FaxResult
        {
            Success = true,
            FaxId = $"MOCK-FAX-{Guid.NewGuid():N}",
            Status = "Queued",
            SentAt = DateTime.UtcNow,
            PageCount = request.PdfContent.Length / 1024 // Rough estimate
        };
    }

    public async Task<FaxResult> GetFaxStatusAsync(string faxId, CancellationToken cancellationToken = default)
    {
        if (!_isEnabled)
        {
            return new FaxResult
            {
                Success = false,
                ErrorMessage = "Fax service is disabled"
            };
        }

        // TODO: Implement actual status retrieval
        await Task.Delay(50, cancellationToken);

        return new FaxResult
        {
            Success = true,
            FaxId = faxId,
            Status = "Sent", // Mock status
            SentAt = DateTime.UtcNow
        };
    }
}

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

        // Current implementation runs in deterministic stub mode.
        // A production implementation should call the HumbleFax API with PDF payloads.

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

        // Stub status lookup path for environments without live fax gateway wiring.
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

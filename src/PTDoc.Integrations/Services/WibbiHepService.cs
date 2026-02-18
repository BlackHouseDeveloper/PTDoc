using Microsoft.Extensions.Configuration;
using PTDoc.Application.Integrations;

namespace PTDoc.Integrations.Services;

/// <summary>
/// Wibbi Home Exercise Program service implementation.
/// </summary>
public class WibbiHepService : IHomeExerciseProgramService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string? _apiKey;
    private readonly bool _isEnabled;

    public WibbiHepService(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _apiKey = configuration["Integrations:Hep:ApiKey"];
        _isEnabled = configuration.GetValue<bool>("Integrations:Hep:Enabled");
    }

    public async Task<HepAssignmentResult> AssignProgramAsync(HepAssignmentRequest request, CancellationToken cancellationToken = default)
    {
        if (!_isEnabled)
        {
            return new HepAssignmentResult
            {
                Success = false,
                ErrorMessage = "HEP service is disabled"
            };
        }

        if (string.IsNullOrEmpty(_apiKey))
        {
            return new HepAssignmentResult
            {
                Success = false,
                ErrorMessage = "HEP service not configured"
            };
        }

        // TODO: Implement actual Wibbi API integration
        // This is a mock implementation for now
        // Production would call Wibbi API to assign program

        // Mock successful assignment for development
        await Task.Delay(150, cancellationToken); // Simulate API call

        return new HepAssignmentResult
        {
            Success = true,
            AssignmentId = $"MOCK-HEP-{Guid.NewGuid():N}",
            PatientPortalUrl = $"https://wibbi.example.com/patient/{request.PatientId}",
            AssignedAt = DateTime.UtcNow
        };
    }

    public async Task<HepAssignmentResult> GetPatientProgramAsync(Guid patientId, CancellationToken cancellationToken = default)
    {
        if (!_isEnabled)
        {
            return new HepAssignmentResult
            {
                Success = false,
                ErrorMessage = "HEP service is disabled"
            };
        }

        // TODO: Implement actual program retrieval
        await Task.Delay(75, cancellationToken);

        return new HepAssignmentResult
        {
            Success = true,
            AssignmentId = $"MOCK-HEP-{patientId:N}",
            PatientPortalUrl = $"https://wibbi.example.com/patient/{patientId}"
        };
    }
}

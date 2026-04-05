using System.Net.Http.Json;
using PTDoc.Application.Identity;

namespace PTDoc.Web.Auth;

public sealed class SignupApiClient
{
    private readonly IHttpClientFactory httpClientFactory;

    public SignupApiClient(IHttpClientFactory httpClientFactory)
    {
        this.httpClientFactory = httpClientFactory;
    }

    public async Task<RegistrationResult> RegisterAsync(
        string fullName,
        string email,
        DateTime dateOfBirth,
        string roleKey,
        Guid? clinicId,
        string pin,
        string licenseNumber,
        string licenseState,
        CancellationToken cancellationToken = default)
    {
        var client = httpClientFactory.CreateClient("PTDocAuthApi");

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new SignupApiRegistrationRequest
            {
                FullName = fullName,
                Email = email,
                DateOfBirth = dateOfBirth,
                RoleKey = roleKey,
                ClinicId = clinicId,
                Pin = pin,
                LicenseNumber = licenseNumber,
                LicenseState = licenseState,
                // TODO: Add license/certification blob IDs here after upload flow is implemented.
            },
            cancellationToken);

        var payload = await response.Content.ReadFromJsonAsync<SignupApiRegistrationResponse>(cancellationToken: cancellationToken);
        if (payload is null)
        {
            return new RegistrationResult(RegistrationStatus.ServerError, null, "Registration response was empty.");
        }

        if (!Enum.TryParse<RegistrationStatus>(payload.Status, true, out var status))
        {
            status = response.IsSuccessStatusCode ? RegistrationStatus.PendingApproval : RegistrationStatus.ServerError;
        }

        return new RegistrationResult(status, payload.UserId, payload.Error);
    }

    public async Task<IReadOnlyList<ClinicSummary>> GetClinicsAsync(CancellationToken cancellationToken = default)
    {
        var client = httpClientFactory.CreateClient("PTDocAuthApi");
        var clinics = await client.GetFromJsonAsync<List<ClinicSummary>>(
            "/api/v1/auth/clinics",
            cancellationToken);

        return clinics ?? [];
    }

    public async Task<IReadOnlyList<RoleSummary>> GetRolesAsync(CancellationToken cancellationToken = default)
    {
        var client = httpClientFactory.CreateClient("PTDocAuthApi");
        var roles = await client.GetFromJsonAsync<List<RoleSummary>>(
            "/api/v1/auth/roles",
            cancellationToken);

        return roles ?? [];
    }

    private sealed class SignupApiRegistrationRequest
    {
        public required string FullName { get; init; }
        public required string Email { get; init; }
        public required DateTime DateOfBirth { get; init; }
        public required string RoleKey { get; init; }
        public Guid? ClinicId { get; init; }
        public required string Pin { get; init; }
        public string? LicenseNumber { get; init; }
        public string? LicenseState { get; init; }
    }

    private sealed class SignupApiRegistrationResponse
    {
        public required string Status { get; init; }
        public Guid? UserId { get; init; }
        public string? Error { get; init; }
    }
}

using System.Net;
using System.Net.Http.Json;
using PTDoc.Application.Identity;

namespace PTDoc.UI.Services;

public sealed class OrganizationApiClient(HttpClient client) : IOrganizationAdministrationService
{
    private const string Root = "/api/v1/organizations";
    public Task<IReadOnlyList<OrganizationSummary>> GetOrganizationsAsync(CancellationToken cancellationToken = default) => Get<OrganizationSummary>(Root + "/", cancellationToken);
    public Task<IReadOnlyList<OrganizationClinic>> GetClinicsAsync(Guid organizationId, CancellationToken cancellationToken = default) => Get<OrganizationClinic>($"{Root}/{organizationId}/clinics", cancellationToken);
    public Task<IReadOnlyList<OrganizationStaff>> GetStaffAsync(Guid organizationId, CancellationToken cancellationToken = default) => Get<OrganizationStaff>($"{Root}/{organizationId}/staff", cancellationToken);
    public Task<IReadOnlyList<ClinicAccess>> GetStaffClinicsAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default) => Get<ClinicAccess>($"{Root}/{organizationId}/staff/{userId}/clinics", cancellationToken);
    public Task<IReadOnlyList<ClinicAccess>> GetMyClinicsAsync(CancellationToken cancellationToken = default) => Get<ClinicAccess>(Root + "/my-clinics", cancellationToken);
    public Task<OrganizationClinic> CreateClinicAsync(Guid organizationId, CreateOrganizationClinic request, CancellationToken cancellationToken = default) => Send<OrganizationClinic>(HttpMethod.Post, $"{Root}/{organizationId}/clinics", request, cancellationToken);
    public Task SetOrganizationMembershipAsync(Guid organizationId, Guid userId, OrganizationMembershipChange request, CancellationToken cancellationToken = default) => Send(HttpMethod.Put, $"{Root}/{organizationId}/staff/{userId}", request, cancellationToken);
    public Task SetClinicMembershipAsync(Guid organizationId, Guid clinicId, Guid userId, ClinicMembershipChange request, CancellationToken cancellationToken = default) => Send(HttpMethod.Put, $"{Root}/{organizationId}/clinics/{clinicId}/staff/{userId}", request, cancellationToken);
    public Task<OrganizationInviteResult> InviteAsync(Guid organizationId, string email, CancellationToken cancellationToken = default) => Send<OrganizationInviteResult>(HttpMethod.Post, $"{Root}/{organizationId}/invitations", new InviteOrganizationStaff(email), cancellationToken);
    public Task AcceptInvitationAsync(string token, CancellationToken cancellationToken = default) => Send(HttpMethod.Post, Root + "/accept-invitation", new AcceptOrganizationInvitation(token), cancellationToken);

    private async Task<IReadOnlyList<T>> Get<T>(string path, CancellationToken ct)
    {
        using var response = await client.GetAsync(path, ct);
        Check(response);
        return await response.Content.ReadFromJsonAsync<T[]>(cancellationToken: ct) ?? [];
    }
    private async Task<T> Send<T>(HttpMethod method, string path, object body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        using var response = await client.SendAsync(request, ct);
        Check(response);
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct) ?? throw new HttpRequestException("The server returned no result.");
    }
    private async Task Send(HttpMethod method, string path, object body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
        using var response = await client.SendAsync(request, ct);
        Check(response);
    }
    private static void Check(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.Conflict) throw new OrganizationConflictException("Access changed or the request conflicts with existing records. Reload before retrying.");
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized) throw new OrganizationAccessException("This action is not permitted. Sign in again or ask an organization owner.");
        if (response.StatusCode == HttpStatusCode.BadRequest) throw new ArgumentException("Check the entered values and try again.");
        response.EnsureSuccessStatusCode();
    }
}

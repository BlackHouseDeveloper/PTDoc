using System.Net.Http.Json;
using System.Text.Json;
using PTDoc.Application.Identity;

namespace PTDoc.UI.Services;

public sealed class SubscriptionApiClient(HttpClient publicClient, HttpClient authenticatedClient)
{
    private const string Root = "/api/v1/subscriptions";
    public async Task<SubscriptionOffer> GetOfferAsync() => await publicClient.GetFromJsonAsync<SubscriptionOffer>(Root + "/offer")
        ?? throw new HttpRequestException("Subscription information is unavailable.");
    public async Task<RegistrationResult> RegisterAsync(IndividualSignupRequest request)
    {
        using var response = await publicClient.PostAsJsonAsync(Root + "/individual", request);
        if (response.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
            return (await response.Content.ReadFromJsonAsync<RegistrationResult>())!;
        await CheckAsync(response);
        return (await response.Content.ReadFromJsonAsync<RegistrationResult>())!;
    }
    public async Task<SubscriptionAccount?> GetAccountAsync()
    {
        using var response = await authenticatedClient.GetAsync(Root + "/account");
        await CheckAsync(response);
        return response.StatusCode == System.Net.HttpStatusCode.NoContent ? null : await response.Content.ReadFromJsonAsync<SubscriptionAccount>();
    }
    public Task<SubscriptionAccount> PayAsync(SubscriptionPaymentRequest request) => PostAsync("/payment", request);
    public Task<SubscriptionAccount> RepairAsync() => PostAsync("/repair", new { });
    public Task<SubscriptionAccount> CancelAsync() => PostAsync("/cancel", new { });
    private async Task<SubscriptionAccount> PostAsync(string path, object body)
    {
        using var response = await authenticatedClient.PostAsJsonAsync(Root + path, body);
        await CheckAsync(response);
        return await response.Content.ReadFromJsonAsync<SubscriptionAccount>() ?? throw new HttpRequestException("Billing response was empty.");
    }
    private static async Task CheckAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        ErrorResponse? error = null;
        try { error = await response.Content.ReadFromJsonAsync<ErrorResponse>(); }
        catch (JsonException) { }
        throw new HttpRequestException(error?.Error ?? "The request could not be completed. Sign in and try again.");
    }
    private sealed record ErrorResponse(string? Error);
}

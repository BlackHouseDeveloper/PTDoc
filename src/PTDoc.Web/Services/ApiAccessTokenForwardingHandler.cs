using System.Net.Http.Headers;

using PTDoc.Application.Identity;

namespace PTDoc.Web.Services;

public sealed class ApiAccessTokenForwardingHandler(
    IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Headers.Authorization is null)
        {
            var accessToken = await GetAccessTokenAsync();
            if (!string.IsNullOrWhiteSpace(accessToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }

    private Task<string?> GetAccessTokenAsync()
    {
        var tokenFromHttpContext = httpContextAccessor.HttpContext?.User.FindFirst(PTDocClaimTypes.ApiAccessToken)?.Value;
        return Task.FromResult(string.IsNullOrWhiteSpace(tokenFromHttpContext) ? null : tokenFromHttpContext);
    }
}

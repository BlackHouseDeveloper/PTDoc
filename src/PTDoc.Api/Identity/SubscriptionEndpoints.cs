using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Identity;
using PTDoc.Infrastructure.Identity;

namespace PTDoc.Api.Identity;

public static class SubscriptionEndpoints
{
    public static void MapSubscriptionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/subscriptions").WithTags("Subscriptions");
        group.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            try { return await next(context); }
            catch (UnauthorizedAccessException) { return Results.Forbid(); }
            catch (ArgumentException exception) { return Results.BadRequest(new { error = exception.Message }); }
            catch (SubscriptionConflictException exception) { return Results.Conflict(new { error = exception.Message }); }
            catch (SubscriptionUnavailableException exception) { return Results.Json(new { error = exception.Message }, statusCode: 503); }
            catch (DbUpdateException) { return Results.Conflict(new { error = "Account or billing state changed. Sign in or reload before continuing." }); }
            catch (HttpRequestException) { return Results.Json(new { error = "Billing could not be confirmed. Refresh billing status before retrying." }, statusCode: 503); }
        });
        group.MapGet("/offer", (ISubscriptionService service) => service.GetOffer()).AllowAnonymous();
        group.MapPost("/individual", async (IndividualSignupRequest request, ISubscriptionService service, CancellationToken ct) =>
        {
            var result = await service.RegisterAsync(request, ct);
            return result.Succeeded ? Results.Ok(result) : Results.UnprocessableEntity(result);
        }).AllowAnonymous().RequireRateLimiting("PinAuthentication");
        group.MapGet("/account", (ISubscriptionService service, CancellationToken ct) => service.GetAccountAsync(ct)).RequireAuthorization();
        group.MapPost("/payment", (SubscriptionPaymentRequest request, ISubscriptionService service, CancellationToken ct) => service.PayAsync(request, ct))
            .RequireAuthorization().RequireRateLimiting("PinAuthentication");
        group.MapPost("/repair", (ISubscriptionService service, CancellationToken ct) => service.RepairAsync(ct)).RequireAuthorization();
        group.MapPost("/cancel", (ISubscriptionService service, CancellationToken ct) => service.CancelAsync(ct)).RequireAuthorization();
        group.MapPost("/authorize-net/webhook", Webhook).AllowAnonymous();
    }

    private static async Task<IResult> Webhook(HttpContext http, IConfiguration configuration, SubscriptionService service, CancellationToken ct)
    {
        // Bound actual bytes read for every request, including chunked bodies,
        // then verify those exact bytes before parsing.
        const int limit = 65536;
        using var body = new MemoryStream();
        var buffer = new byte[4096];
        int read;
        while ((read = await http.Request.Body.ReadAsync(buffer, ct)) > 0)
        {
            if (body.Length + read > limit) return Results.StatusCode(413);
            body.Write(buffer, 0, read);
        }
        var bytes = body.ToArray();
        if (!VerifySignature(bytes, http.Request.Headers["X-ANET-Signature"].ToString(),
                configuration["Subscriptions:AuthorizeNet:SignatureKey"])) return Results.Unauthorized();
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            var type = root.GetProperty("eventType").GetString() ?? string.Empty;
            var id = root.GetProperty("payload").GetProperty("id").ToString();
            if (string.IsNullOrEmpty(id) || id.Length > 64 || !id.All(char.IsAsciiDigit)) return Results.BadRequest();
            if (type.StartsWith("net.authorize.payment.", StringComparison.Ordinal))
                await service.ReceiveTransactionAsync(id, ct);
            else if (type.StartsWith("net.authorize.customer.subscription.", StringComparison.Ordinal))
                await service.ReceiveRecurringAsync(id, ct);
            return Results.Ok();
        }
        catch (JsonException) { return Results.BadRequest(); }
        catch (KeyNotFoundException) { return Results.BadRequest(); }
        // Retry failed persistence/provider verification. No event is acknowledged early.
    }

    public static bool VerifySignature(byte[] body, string signature, string? key)
    {
        if (key?.Length != 128 || !signature.StartsWith("sha512=", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            var expected = HMACSHA512.HashData(Convert.FromHexString(key), body);
            var supplied = Convert.FromHexString(signature[7..]);
            return CryptographicOperations.FixedTimeEquals(expected, supplied);
        }
        catch (FormatException) { return false; }
    }
}

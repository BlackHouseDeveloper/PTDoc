using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Insurance;
using PTDoc.Application.Services;
using PTDoc.Core.Models;

namespace PTDoc.Api.Insurance;

public static class InsurancePolicyEndpoints
{
    public static void MapInsurancePolicyEndpoints(this IEndpointRouteBuilder app)
    {
        var read = app.MapGroup("/api/v1/patients/{patientId:guid}/insurance-policies").WithTags("Insurance Policies").RequireAuthorization(AuthorizationPolicies.InsurancePolicyRead);
        read.MapGet("/", async (Guid patientId, bool? includeArchived, IInsurancePolicyService service, CancellationToken ct) => await Execute(() => service.ListAsync(patientId, includeArchived == true, ct)));
        var write = app.MapGroup("/api/v1/patients/{patientId:guid}/insurance-policies").WithTags("Insurance Policies").RequireAuthorization(AuthorizationPolicies.InsurancePolicyWrite);
        write.MapPost("/", async (Guid patientId, UpsertInsurancePolicyRequest request, IInsurancePolicyService service, CancellationToken ct) => await Execute(() => service.UpsertPolicyAsync(patientId, null, request, ct)));
        write.MapPut("/{policyId:guid}", async (Guid patientId, Guid policyId, UpsertInsurancePolicyRequest request, IInsurancePolicyService service, CancellationToken ct) => await Execute(() => service.UpsertPolicyAsync(patientId, policyId, request, ct)));
        write.MapDelete("/{policyId:guid}", async (Guid patientId, Guid policyId, IInsurancePolicyService service, CancellationToken ct) => await Execute(async () => { await service.ArchivePolicyAsync(patientId, policyId, ct); return true; }));
        write.MapPut("/priority", async (Guid patientId, Dictionary<Guid, InsuranceCoveragePriority> priorities, IInsurancePolicyService service, CancellationToken ct) => await Execute(async () => { await service.ReorderAsync(patientId, priorities, ct); return true; }));
        write.MapPost("/{policyId:guid}/authorizations", async (Guid patientId, Guid policyId, UpsertInsuranceAuthorizationRequest request, IInsurancePolicyService service, CancellationToken ct) => await Execute(() => service.UpsertAuthorizationAsync(patientId, policyId, null, request, ct)));
        write.MapPut("/{policyId:guid}/authorizations/{authorizationId:guid}", async (Guid patientId, Guid policyId, Guid authorizationId, UpsertInsuranceAuthorizationRequest request, IInsurancePolicyService service, CancellationToken ct) => await Execute(() => service.UpsertAuthorizationAsync(patientId, policyId, authorizationId, request, ct)));
        write.MapDelete("/{policyId:guid}/authorizations/{authorizationId:guid}", async (Guid patientId, Guid policyId, Guid authorizationId, IInsurancePolicyService service, CancellationToken ct) => await Execute(async () => { await service.ArchiveAuthorizationAsync(patientId, policyId, authorizationId, ct); return true; }));
        app.MapPost("/api/v1/admin/insurance-policies/backfill", async (IInsurancePolicyService service, CancellationToken ct) => await Execute(() => service.BackfillLegacyPayerDataAsync(ct))).WithTags("Insurance Policy Administration").RequireAuthorization(AuthorizationPolicies.ProviderDirectoryAdmin);
    }
    private static async Task<IResult> Execute<T>(Func<Task<T>> action)
    {
        try
        {
            return Results.Ok(await action());
        }
        catch (KeyNotFoundException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
        catch (DbUpdateConcurrencyException ex)
        {
            return Results.Conflict(new { error = ex.Message });
        }
        catch (DbUpdateException)
        {
            return Results.Conflict(new
            {
                error = "The insurance change conflicts with a newer policy record. Refresh and try again."
            });
        }
        catch (ArgumentException ex)
        {
            return Results.UnprocessableEntity(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Results.Conflict(new { error = ex.Message });
        }
    }
}

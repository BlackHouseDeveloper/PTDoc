using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Providers;
using PTDoc.Application.Services;
using PTDoc.Core.Models;

namespace PTDoc.Api.Providers;

public static class ProviderDirectoryEndpoints
{
    public static void MapProviderDirectoryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/providers", async ([FromQuery]string? q,[FromQuery]int? take,IProviderDirectoryService service,CancellationToken ct)=>Results.Ok(await service.SearchAsync(q,ProviderDirectoryStatus.Active,take??25,ct)))
            .WithTags("Provider Directory").RequireAuthorization(AuthorizationPolicies.ProviderDirectorySearch);
        var read=app.MapGroup("/api/v1/providers").WithTags("Provider Directory").RequireAuthorization(AuthorizationPolicies.ProviderDirectoryRead);
        read.MapGet("/{providerId:guid}", async (Guid providerId,IProviderDirectoryService service,CancellationToken ct)=>await service.GetAsync(providerId,ct) is { } provider?Results.Ok(provider):Results.NotFound());
        read.MapGet("/patients/{patientId:guid}", async (Guid patientId,IProviderDirectoryService service,CancellationToken ct)=>await Execute(()=>service.ListPatientRelationshipsAsync(patientId,ct)));

        var submit=app.MapGroup("/api/v1/providers").WithTags("Provider Directory").RequireAuthorization(AuthorizationPolicies.ProviderDirectorySubmit);
        submit.MapPost("/candidates", async (SubmitProviderCandidateRequest request,IProviderDirectoryService service,CancellationToken ct)=>await Execute(()=>service.SubmitAsync(request,ct),created:true));
        submit.MapPut("/candidates/{providerId:guid}", async (Guid providerId,UpdateProviderCandidateRequest request,IProviderDirectoryService service,CancellationToken ct)=>await Execute(()=>service.UpdateAsync(providerId,request,ct)));
        submit.MapPost("/patients/{patientId:guid}", async (Guid patientId,UpsertPatientProviderRelationshipRequest request,IProviderDirectoryService service,CancellationToken ct)=>await Execute(()=>service.UpsertPatientRelationshipAsync(patientId,null,request,ct),created:true));
        submit.MapPut("/patients/{patientId:guid}/{relationshipId:guid}", async (Guid patientId,Guid relationshipId,UpsertPatientProviderRelationshipRequest request,IProviderDirectoryService service,CancellationToken ct)=>await Execute(()=>service.UpsertPatientRelationshipAsync(patientId,relationshipId,request,ct)));
        submit.MapDelete("/patients/{patientId:guid}/{relationshipId:guid}", async (Guid patientId,Guid relationshipId,IProviderDirectoryService service,CancellationToken ct)=>await Execute(async()=>{await service.ArchivePatientRelationshipAsync(patientId,relationshipId,ct);return true;}));

        var admin=app.MapGroup("/api/v1/admin/providers").WithTags("Provider Directory Administration").RequireAuthorization(AuthorizationPolicies.ProviderDirectoryAdmin);
        admin.MapGet("/", async ([FromQuery]string? q,[FromQuery]ProviderDirectoryStatus? status,[FromQuery]int? take,IProviderDirectoryService service,CancellationToken ct)=>Results.Ok(await service.SearchForAdministrationAsync(q,status,take??25,ct)));
        admin.MapPost("/{providerId:guid}/approve", async (Guid providerId,ProviderDecisionRequest request,IProviderDirectoryService service,CancellationToken ct)=>await Execute(()=>service.ApproveAsync(providerId,request,ct)));
        admin.MapPost("/{providerId:guid}/reject", async (Guid providerId,ProviderDecisionRequest request,IProviderDirectoryService service,CancellationToken ct)=>await Execute(()=>service.RejectAsync(providerId,request,ct)));
        admin.MapPost("/{providerId:guid}/archive", async (Guid providerId,ProviderDecisionRequest request,IProviderDirectoryService service,CancellationToken ct)=>await Execute(async()=>{await service.ArchiveAsync(providerId,request,ct);return true;}));
    }

    private static async Task<IResult> Execute<T>(Func<Task<T>> action,bool created=false)
    {
        try{var value=await action();return created?Results.Created(string.Empty,value):Results.Ok(value);}
        catch(KeyNotFoundException ex){return Results.NotFound(new{error=ex.Message});}
        catch(DbUpdateConcurrencyException ex){return Results.Conflict(new{error=ex.Message});}
        catch(DbUpdateException){return Results.Conflict(new{error="The provider change conflicts with a newer directory record. Refresh and review the current entries."});}
        catch(ArgumentException ex){return Results.UnprocessableEntity(new{error=ex.Message});}
        catch(InvalidOperationException ex){return Results.Conflict(new{error=ex.Message});}
    }
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.AI;
using PTDoc.Application.Compliance;
using PTDoc.Application.Identity;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Api.AI;

public static class NoteNarrativeEndpoint
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<IResult> Generate(
        [FromBody] NoteNarrativeRequest request, ApplicationDbContext db, ITenantContextAccessor tenant,
        INoteNarrativeGenerator generator, IAuditService audit, IConfiguration configuration,
        HttpContext httpContext, CancellationToken cancellationToken)
    {
        IResult Error(int status, string message, string code) => Results.Json(new
            { error = message, code, correlationId = httpContext.TraceIdentifier }, statusCode: status);
        if (!configuration.GetValue<bool>("FeatureFlags:EnableAiGeneration"))
            return Error(403, "AI generation is currently disabled.", "ai_feature_disabled");
        if (request.NoteId == Guid.Empty || request.ExpectedLastModifiedUtc == default || !Enum.IsDefined(request.Purpose))
            return Error(400, "A saved note version and valid purpose are required.", "ai_request_invalid");
        var clinicId = tenant.GetCurrentClinicId();
        if (!clinicId.HasValue) return Error(403, "Clinic context is required.", "ai_clinic_required");
        var note = await db.ClinicalNotes.AsNoTracking()
            .Where(n => n.Id == request.NoteId && n.Patient != null && n.Patient.ClinicId == clinicId
                && (n.ClinicId == clinicId || n.ClinicId == null))
            .FirstOrDefaultAsync(cancellationToken);
        if (note is null) return Error(404, "Note not found.", "ai_note_not_found");
        if (note.NoteStatus != NoteStatus.Draft || note.IsFinalized)
            return Error(409, "Generation requires an editable draft note.", "ai_signed_note");
        if (note.LastModifiedUtc != request.ExpectedLastModifiedUtc)
            return Error(409, "The saved note changed. Save and generate again.", "ai_source_changed");
        NoteWorkspaceV2Payload? payload;
        try
        {
            using var document = JsonDocument.Parse(note.ContentJson);
            if (!document.RootElement.TryGetProperty("schemaVersion", out var schema) || schema.GetInt32() != WorkspaceSchemaVersions.EvalReevalProgressV2)
                return Error(400, "Save this note in the current workspace before generating.", "ai_source_schema");
            payload = JsonSerializer.Deserialize<NoteWorkspaceV2Payload>(note.ContentJson, JsonOptions);
            if (payload is null || payload.Subjective is null || payload.Objective is null || payload.Assessment is null || payload.Plan is null)
                return Error(400, "Saved note content is incomplete.", "ai_source_schema");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        { return Error(400, "Saved note content could not be read.", "ai_source_schema"); }

        var sourceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(note.ContentJson))).ToLowerInvariant();
        var sections = new List<NoteNarrativeSection>();
        List<(NoteNarrativePurpose Purpose, IReadOnlyList<NoteNarrativeInput> Inputs)> contexts;
        try
        {
            contexts = NoteNarrativeContextBuilder.SectionsFor(request.Purpose)
                .Select(p => (p, NoteNarrativeContextBuilder.Build(payload, p))).ToList();
        }
        catch (Exception ex) when (ex is NullReferenceException or ArgumentNullException)
        { return Error(400, "Saved note content is incomplete.", "ai_source_schema"); }
        if (contexts.Any(c => c.Inputs.Sum(i => i.Label.Length + i.Value.Length) > 24000))
            return Error(400, "This section is too large for generation. Continue editing manually.", "ai_source_too_large");
        foreach (var (purpose, inputs) in contexts)
        {
            AiResult result;
            try { result = await generator.GenerateSectionAsync(purpose, inputs, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch { result = new AiResult { Success = false, GeneratedText = string.Empty, Metadata = null! }; }
            var metadata = result.Metadata ?? new AiPromptMetadata { TemplateVersion = "unknown", Model = "unknown", GeneratedAtUtc = DateTime.UtcNow };
            var id = (httpContext.User.FindFirst(PTDocClaimTypes.InternalUserId)
                ?? httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier))?.Value;
            if (Guid.TryParse(id, out var userId))
            {
                var auditEvent = AuditEvent.AiGenerationAttempt(note.Id, purpose.ToString(), metadata.Model,
                    userId, result.Success, result.Success ? null : "section_generation_failed");
                auditEvent.Metadata["TemplateVersion"] = metadata.TemplateVersion;
                auditEvent.Metadata["SourceSha256"] = sourceHash;
                await audit.LogAiGenerationAttemptAsync(auditEvent, cancellationToken);
            }
            if (!result.Success || string.IsNullOrWhiteSpace(result.GeneratedText))
                return Error(502, "Unable to generate this section. You can continue editing manually.", "ai_generation_failed");
            sections.Add(new(purpose, result.GeneratedText, metadata));
        }
        // Re-check both source and signature after provider latency; never hand back stale clinical suggestions.
        var current = await db.ClinicalNotes.AsNoTracking().FirstOrDefaultAsync(n => n.Id == note.Id && n.Patient != null && n.Patient.ClinicId == clinicId
                && (n.ClinicId == clinicId || n.ClinicId == null), cancellationToken);
        if (current is null || current.NoteStatus != NoteStatus.Draft || current.IsFinalized
            || current.LastModifiedUtc != note.LastModifiedUtc || current.ContentJson != note.ContentJson)
            return Error(409, "The note changed during generation. Save and generate again.", "ai_source_changed");
        return Results.Ok(new NoteNarrativeResult { Success = true, NoteId = note.Id, Purpose = request.Purpose,
            SourceLastModifiedUtc = note.LastModifiedUtc, SourceSha256 = sourceHash, Sections = sections.AsReadOnly() });
    }
}

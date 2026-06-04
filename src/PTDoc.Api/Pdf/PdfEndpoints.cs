using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PTDoc.Application.Compliance;
using PTDoc.Application.Pdf;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace PTDoc.Api.Pdf;

public static class PdfEndpoints
{
    public static void MapPdfEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/notes")
            .RequireAuthorization(AuthorizationPolicies.NoteExport)
            .WithTags("PDF Export");

        group.MapGet("/{noteId:guid}/export/hierarchy", GetNoteDocumentHierarchy)
            .RequireAuthorization(AuthorizationPolicies.NoteExport)
            .WithName("GetNoteDocumentHierarchy");

        group.MapPost("/{noteId:guid}/export/pdf", ExportNoteToPdf)
            .WithName("ExportNoteToPdf");
    }

    private static async Task<IResult> GetNoteDocumentHierarchy(
        [FromRoute] Guid noteId,
        [FromServices] IClinicalDocumentHierarchyBuilder hierarchyBuilder,
        [FromServices] ApplicationDbContext dbContext)
    {
        var noteData = await LoadNoteExportDtoAsync(dbContext, noteId);
        if (noteData is null)
        {
            return Results.NotFound(new { error = "Clinical note not found" });
        }

        var readiness = ValidateExportReadiness(noteData);
        if (!readiness.IsReady)
        {
            return Results.UnprocessableEntity(new
            {
                error = readiness.ErrorMessage,
                noteId
            });
        }

        var hierarchy = hierarchyBuilder.Build(noteData);
        return Results.Ok(hierarchy);
    }

    private static async Task<IResult> ExportNoteToPdf(
        [FromRoute] Guid noteId,
        [FromServices] IPdfRenderer pdfRenderer,
        [FromServices] IAuditService auditService,
        [FromServices] ApplicationDbContext dbContext,
        [FromServices] ILoggerFactory loggerFactory,
        HttpContext httpContext)
    {
        var logger = loggerFactory.CreateLogger("PTDoc.Api.Pdf.PdfEndpoints");

        try
        {
            var noteData = await LoadNoteExportDtoAsync(dbContext, noteId);
            if (noteData is null)
            {
                return Results.NotFound(new { error = "Clinical note not found" });
            }

            var readiness = ValidateExportReadiness(noteData);
            if (!readiness.IsReady)
            {
                return Results.UnprocessableEntity(new
                {
                    error = readiness.ErrorMessage,
                    noteId
                });
            }

            // Export options
            noteData.IncludeMedicareCompliance = true;
            noteData.IncludeSignatureBlock = true;

            // Renderer receives DTO with NO database access
            var result = await pdfRenderer.ExportNoteToPdfAsync(noteData);

            // Audit PDF export (NO PHI - only metadata)
            var userId = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "system";
            if (Guid.TryParse(userId, out var userGuid))
            {
                await auditService.LogRuleEvaluationAsync(
                    new AuditEvent
                    {
                        EventType = "PdfExport",
                        UserId = userGuid,
                        Metadata = new Dictionary<string, object>
                        {
                            ["NoteId"] = noteId,
                            ["FileSizeBytes"] = result.FileSizeBytes,
                            ["ExportedAt"] = DateTime.UtcNow
                        }
                    });
            }

            return Results.File(
                result.PdfBytes,
                result.ContentType,
                result.FileName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "PDF export failed for note {NoteId}", noteId);

            return Results.Problem(
                title: "PDF Export Failed",
                detail: "The server could not generate this PDF. Retry after confirming the note content is complete.",
                statusCode: 500);
        }
    }

    private static async Task<NoteExportDto?> LoadNoteExportDtoAsync(ApplicationDbContext dbContext, Guid noteId)
    {
        var note = await dbContext.ClinicalNotes
            .Include(n => n.Patient)
            .Include(n => n.Clinic)
            .FirstOrDefaultAsync(n => n.Id == noteId);

        if (note is null)
        {
            return null;
        }

        var noteData = new NoteExportDto
        {
            NoteId = note.Id,
            IsAddendum = note.IsAddendum,
            ParentNoteId = note.ParentNoteId,
            NoteType = note.NoteType,
            IsReEvaluation = note.IsReEvaluation,
            NoteStatus = note.NoteStatus,
            DateOfService = note.DateOfService,
            NoteTypeDisplayName = ToDisplayName(note.NoteType, note.IsReEvaluation),
            ExportStatusLabel = ToExportStatusLabel(note.NoteStatus),
            ExportStatusWatermark = ToExportStatusWatermark(note.NoteStatus),
            ContentJson = NoteWriteService.NormalizeContentJson(
                note.NoteType,
                note.IsReEvaluation,
                note.DateOfService,
                note.ContentJson),
            CptCodesJson = note.CptCodesJson ?? "[]",
            TotalTreatmentMinutes = note.TotalTreatmentMinutes,
            ClinicName = note.Clinic?.Name ?? string.Empty,
            PatientFirstName = note.Patient?.FirstName ?? string.Empty,
            PatientLastName = note.Patient?.LastName ?? string.Empty,
            PatientDateOfBirth = note.Patient?.DateOfBirth,
            PatientMedicalRecordNumber = note.Patient?.MedicalRecordNumber ?? string.Empty,
            PatientDiagnosisCodesJson = note.Patient?.DiagnosisCodesJson ?? "[]",
            ReferringPhysician = note.Patient?.ReferringPhysician,
            ReferringPhysicianNpi = note.Patient?.PhysicianNpi,
            SignatureHash = note.SignatureHash,
            SignedUtc = note.SignedUtc,
            SignedByUserId = note.SignedByUserId,
            TherapistNpi = note.TherapistNpi,
            PhysicianSignatureHash = note.PhysicianSignatureHash,
            PhysicianSignedUtc = note.PhysicianSignedUtc
        };

        var (clinicianDisplayName, clinicianCredentials) = await ResolveClinicianInfoAsync(
            dbContext,
            note.SignedByUserId ?? note.ModifiedByUserId);
        noteData.ClinicianDisplayName = clinicianDisplayName;
        noteData.ClinicianCredentials = clinicianCredentials;
        return noteData;
    }

    private static string ToDisplayName(NoteType noteType, bool isReEvaluation) => noteType switch
    {
        NoteType.Evaluation => isReEvaluation
            ? "Physical Therapy Re-Evaluation"
            : "Physical Therapy Initial Evaluation",
        NoteType.ProgressNote => "Physical Therapy Progress Note",
        NoteType.Daily => "Physical Therapy Daily Note",
        NoteType.Discharge => "Physical Therapy Discharge Summary",
        _ => noteType.ToString()
    };

    private static string ToExportStatusLabel(NoteStatus noteStatus) => noteStatus switch
    {
        NoteStatus.Signed => "Signed",
        NoteStatus.PendingCoSign => "Pending co-sign",
        _ => "Draft"
    };

    private static string ToExportStatusWatermark(NoteStatus noteStatus) => noteStatus switch
    {
        NoteStatus.Signed => string.Empty,
        NoteStatus.PendingCoSign => "PENDING CO-SIGN",
        _ => "DRAFT"
    };

    private static ExportReadinessResult ValidateExportReadiness(NoteExportDto noteData)
    {
        if (!TryParseJson(noteData.ContentJson, out var document))
        {
            return ExportReadinessResult.NotReady(
                "This note cannot be exported because its clinical content is not valid JSON.");
        }

        using (var parsedDocument = document!)
        {
            if (!ContainsClinicalContent(parsedDocument.RootElement, noteData.NoteType))
            {
                return ExportReadinessResult.NotReady(
                    "This note cannot be exported because it does not contain clinical documentation yet.");
            }
        }

        return ExportReadinessResult.Ready;
    }

    private static bool TryParseJson(string contentJson, out JsonDocument? document)
    {
        document = null;
        if (string.IsNullOrWhiteSpace(contentJson))
        {
            return false;
        }

        try
        {
            document = JsonDocument.Parse(contentJson);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool ContainsClinicalContent(
        JsonElement element,
        NoteType noteType,
        string? propertyName = null,
        string? propertyPath = null,
        JsonElement? parent = null)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                return element.EnumerateObject()
                    .Any(property => ContainsClinicalContent(
                        property.Value,
                        noteType,
                        property.Name,
                        BuildPropertyPath(propertyPath, property.Name),
                        element));
            case JsonValueKind.Array:
                return element.EnumerateArray()
                    .Any(item => ContainsClinicalContent(item, noteType, propertyName, propertyPath, parent));
            case JsonValueKind.String:
                return IsClinicalString(propertyName, element.GetString());
            case JsonValueKind.Number:
                return IsClinicalNumber(noteType, propertyName, propertyPath, element, parent);
            case JsonValueKind.True:
                return IsClinicalBoolean(propertyName);
            case JsonValueKind.False:
                return IsClinicalBooleanFalse(propertyName);
            default:
                return false;
        }
    }

    private static bool IsClinicalString(string? propertyName, string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || IsNonClinicalProperty(propertyName))
        {
            return false;
        }

        return true;
    }

    private static bool IsClinicalNumber(
        NoteType noteType,
        string? propertyName,
        string? propertyPath,
        JsonElement element,
        JsonElement? parent)
    {
        if (propertyName is null
            || IsNonClinicalProperty(propertyName))
        {
            return false;
        }

        if (IsSubjectivePainScorePath(propertyPath))
        {
            return element.TryGetDouble(out _)
                && parent.HasValue
                && TryGetBooleanProperty(parent.Value, "IsPainScoreDocumented", out var isDocumented)
                && isDocumented;
        }

        if (IsProgressQuestionnairePainLevelPath(propertyPath))
        {
            return noteType is NoteType.ProgressNote or NoteType.Discharge
                && element.TryGetDouble(out var value)
                && value > 0;
        }

        var isClinicalNumericField = propertyName.Contains("score", StringComparison.OrdinalIgnoreCase)
            || propertyName.Contains("level", StringComparison.OrdinalIgnoreCase)
            || propertyName.Contains("pain", StringComparison.OrdinalIgnoreCase);

        if (!isClinicalNumericField)
        {
            return false;
        }

        return element.TryGetDouble(out _);
    }

    private static string BuildPropertyPath(string? parentPath, string propertyName)
        => string.IsNullOrWhiteSpace(parentPath)
            ? propertyName
            : $"{parentPath}.{propertyName}";

    private static bool IsSubjectivePainScorePath(string? propertyPath)
        => propertyPath is not null
            && (propertyPath.Equals("subjective.currentPainScore", StringComparison.OrdinalIgnoreCase)
                || propertyPath.Equals("subjective.bestPainScore", StringComparison.OrdinalIgnoreCase)
                || propertyPath.Equals("subjective.worstPainScore", StringComparison.OrdinalIgnoreCase));

    private static bool IsProgressQuestionnairePainLevelPath(string? propertyPath)
        => propertyPath is not null
            && (propertyPath.Equals("progressQuestionnaire.currentPainLevel", StringComparison.OrdinalIgnoreCase)
                || propertyPath.Equals("progressQuestionnaire.bestPainLevel", StringComparison.OrdinalIgnoreCase)
                || propertyPath.Equals("progressQuestionnaire.worstPainLevel", StringComparison.OrdinalIgnoreCase));

    private static bool TryGetBooleanProperty(JsonElement element, string propertyName, out bool value)
    {
        value = false;
        foreach (var property in element.EnumerateObject())
        {
            if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                value = property.Value.GetBoolean();
                return true;
            }

            return false;
        }

        return false;
    }

    private static bool IsClinicalBoolean(string? propertyName)
        => propertyName is not null
            && !IsNonClinicalProperty(propertyName)
            && (propertyName.StartsWith("has", StringComparison.OrdinalIgnoreCase)
                || propertyName.StartsWith("uses", StringComparison.OrdinalIgnoreCase)
                || propertyName.StartsWith("taking", StringComparison.OrdinalIgnoreCase)
                || propertyName.EndsWith("documented", StringComparison.OrdinalIgnoreCase)
                || propertyName.EndsWith("normalLimits", StringComparison.OrdinalIgnoreCase));

    private static bool IsClinicalBooleanFalse(string? propertyName)
        => propertyName is not null
            && !IsNonClinicalProperty(propertyName)
            && ExplicitClinicalFalseBooleanProperties.Contains(propertyName);

    private static bool IsNonClinicalProperty(string? propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return false;
        }

        return NonClinicalContentProperties.Contains(propertyName);
    }

    private static readonly HashSet<string> NonClinicalContentProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "id",
        "schemaVersion",
        "noteType",
        "seedContext",
        "kind",
        "sourceIntakeId",
        "fromLockedSubmittedIntake",
        "sourceNoteId",
        "sourceNoteType",
        "sourceReferenceDateUtc",
        "createdUtc",
        "lastModifiedUtc",
        "modifiedByUserId",
        "recordedAtUtc",
        "dateOfService",
        "dateOfEvaluation",
        "dateOfTreatment",
        "startDate",
        "endDate"
    };

    private static readonly HashSet<string> ExplicitClinicalFalseBooleanProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "takingMedications",
        "hasImaging"
    };

    private static async Task<(string DisplayName, string Credentials)> ResolveClinicianInfoAsync(ApplicationDbContext dbContext, Guid? userId)
    {
        if (!userId.HasValue)
        {
            return (string.Empty, string.Empty);
        }

        var user = await dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == userId.Value);

        if (user is null)
        {
            return (string.Empty, string.Empty);
        }

        var displayName = $"{user.FirstName} {user.LastName}".Trim();
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = user.Username;
        }

        return (displayName, user.Role ?? string.Empty);
    }

    private readonly record struct ExportReadinessResult(bool IsReady, string ErrorMessage)
    {
        public static readonly ExportReadinessResult Ready = new(true, string.Empty);

        public static ExportReadinessResult NotReady(string errorMessage) => new(false, errorMessage);
    }
}

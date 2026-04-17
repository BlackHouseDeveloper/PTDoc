using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Compliance;
using PTDoc.Application.Pdf;
using PTDoc.Application.Services;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Services;
using System;
using System.Collections.Generic;
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

        // Enforce finalized-only: hierarchy preview is part of the export surface and must
        // not be available for draft or pending-co-sign notes.
        if (noteData.NoteStatus != PTDoc.Core.Models.NoteStatus.Signed)
        {
            return Results.UnprocessableEntity(new
            {
                error = "Only finalized (signed) notes can be exported as PDF. Sign the note before exporting.",
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
        HttpContext httpContext)
    {
        try
        {
            var noteData = await LoadNoteExportDtoAsync(dbContext, noteId);
            if (noteData is null)
            {
                return Results.NotFound(new { error = "Clinical note not found" });
            }

            // Enforce finalized-only export: unsigned notes must not be exported per Medicare rules.
            // Exporting a draft note could expose incomplete or uncertified clinical content.
            if (noteData.NoteStatus != PTDoc.Core.Models.NoteStatus.Signed)
            {
                return Results.UnprocessableEntity(new
                {
                    error = "Only finalized (signed) notes can be exported as PDF. Sign the note before exporting.",
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
            return Results.Problem(
                title: "PDF Export Failed",
                detail: ex.Message,
                statusCode: 500);
        }
    }

    private static async Task<NoteExportDto?> LoadNoteExportDtoAsync(ApplicationDbContext dbContext, Guid noteId)
    {
        var note = await dbContext.ClinicalNotes
            .Include(n => n.Patient)
            .FirstOrDefaultAsync(n => n.Id == noteId);

        if (note is null)
        {
            return null;
        }

        var noteData = new NoteExportDto
        {
            NoteId = note.Id,
            NoteType = note.NoteType,
            NoteStatus = note.NoteStatus,
            DateOfService = note.DateOfService,
            NoteTypeDisplayName = ToDisplayName(note.NoteType),
            ContentJson = NoteWriteService.NormalizeContentJson(
                note.NoteType,
                note.IsReEvaluation,
                note.DateOfService,
                note.ContentJson),
            CptCodesJson = note.CptCodesJson ?? "[]",
            TotalTreatmentMinutes = note.TotalTreatmentMinutes,
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

        var (clinicianDisplayName, clinicianCredentials) = await ResolveClinicianInfoAsync(dbContext, note.SignedByUserId);
        noteData.ClinicianDisplayName = clinicianDisplayName;
        noteData.ClinicianCredentials = clinicianCredentials;
        return noteData;
    }

    private static string ToDisplayName(PTDoc.Core.Models.NoteType noteType) => noteType switch
    {
        PTDoc.Core.Models.NoteType.Evaluation => "Physical Therapy Initial Evaluation",
        PTDoc.Core.Models.NoteType.ProgressNote => "Physical Therapy Progress Note",
        PTDoc.Core.Models.NoteType.Daily => "Physical Therapy Daily Note",
        PTDoc.Core.Models.NoteType.Discharge => "Physical Therapy Discharge Summary",
        _ => noteType.ToString()
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
}

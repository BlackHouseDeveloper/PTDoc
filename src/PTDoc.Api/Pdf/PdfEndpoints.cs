using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Compliance;
using PTDoc.Application.Pdf;
using PTDoc.Application.Services;
using PTDoc.Infrastructure.Data;
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

        group.MapPost("/{noteId:guid}/export/pdf", ExportNoteToPdf)
            .WithName("ExportNoteToPdf");
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
            // Load note with related data from database
            var note = await dbContext.ClinicalNotes
                .Include(n => n.Patient)
                .FirstOrDefaultAsync(n => n.Id == noteId);

            if (note == null)
            {
                return Results.NotFound(new { error = "Clinical note not found" });
            }

            // Enforce finalized-only export: unsigned notes must not be exported per Medicare rules.
            // Exporting a draft note could expose incomplete or uncertified clinical content.
            if (note.SignatureHash is null)
            {
                return Results.UnprocessableEntity(new
                {
                    error = "Only finalized (signed) notes can be exported as PDF. Sign the note before exporting.",
                    noteId
                });
            }

            // Map to DTO (Clean Architecture: endpoint loads data, renderer receives DTO)
            var noteData = new NoteExportDto
            {
                NoteId = note.Id,
                DateOfService = note.DateOfService,
                NoteTypeDisplayName = note.NoteType.ToString(),
                ContentJson = note.ContentJson ?? "{}",
                CptCodesJson = note.CptCodesJson ?? "[]",

                // Patient information
                PatientFirstName = note.Patient?.FirstName ?? string.Empty,
                PatientLastName = note.Patient?.LastName ?? string.Empty,
                PatientMedicalRecordNumber = note.Patient?.MedicalRecordNumber ?? string.Empty,

                // Signature information
                SignatureHash = note.SignatureHash,
                SignedUtc = note.SignedUtc,
                SignedByUserId = note.SignedByUserId,
                ClinicianDisplayName = await ResolveClinicianDisplayNameAsync(dbContext, note.SignedByUserId),
                ClinicianCredentials = await ResolveClinicianCredentialsAsync(dbContext, note.SignedByUserId),

                // Export options
                IncludeMedicareCompliance = true,
                IncludeSignatureBlock = true
            };

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

    private static async Task<string> ResolveClinicianDisplayNameAsync(ApplicationDbContext dbContext, Guid? userId)
    {
        if (!userId.HasValue)
        {
            return string.Empty;
        }

        var user = await dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == userId.Value);

        if (user is null)
        {
            return string.Empty;
        }

        var name = $"{user.FirstName} {user.LastName}".Trim();
        return string.IsNullOrWhiteSpace(name) ? user.Username : name;
    }

    private static async Task<string> ResolveClinicianCredentialsAsync(ApplicationDbContext dbContext, Guid? userId)
    {
        if (!userId.HasValue)
        {
            return string.Empty;
        }

        var user = await dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == userId.Value);

        return user?.Role ?? string.Empty;
    }
}

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PTDoc.Application.Compliance;
using PTDoc.Application.Pdf;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PTDoc.Api.Pdf;

public static class PdfEndpoints
{
    public static void MapPdfEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/notes")
            .RequireAuthorization()
            .WithTags("PDF Export");
        
        group.MapPost("/{noteId}/export/pdf", ExportNoteToPdf)
            .WithName("ExportNoteToPdf");
    }
    
    private static async Task<IResult> ExportNoteToPdf(
        [FromRoute] Guid noteId,
        [FromServices] IPdfRenderer pdfRenderer,
        [FromServices] IAuditService auditService,
        HttpContext httpContext)
    {
        try
        {
            var request = new PdfExportRequest
            {
                NoteId = noteId,
                IncludeMedicareCompliance = true,
                IncludeSignatureBlock = true
            };
            
            var result = await pdfRenderer.ExportNoteToPdfAsync(request);
            
            // Audit PDF export (NO PHI - only metadata)
            var userId = httpContext.User.FindFirst("sub")?.Value ?? "system";
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
}

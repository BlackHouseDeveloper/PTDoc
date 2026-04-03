using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Compliance;
using PTDoc.Application.DTOs;
using PTDoc.Application.Identity;
using PTDoc.Application.Services;
using PTDoc.Application.Sync;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Infrastructure.Services;

public sealed class NoteWriteService(
    ApplicationDbContext db,
    ITenantContextAccessor tenantContext,
    IIdentityContextAccessor identityContext,
    IAuditService auditService,
    INoteSaveValidationService validationService,
    ISyncEngine syncEngine) : INoteWriteService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<NoteOperationResponse> CreateAsync(CreateNoteRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.TotalMinutes < 0)
        {
            throw new ArgumentException("TotalMinutes must be zero or greater.", nameof(request));
        }

        var cptEntries = TryDeserializeCptCodes(request.CptCodesJson);
        if (cptEntries is null)
        {
            throw new ArgumentException("CptCodesJson is not valid JSON.", nameof(request));
        }

        var validation = await validationService.ValidateAsync(new NoteSaveComplianceRequest
        {
            PatientId = request.PatientId,
            NoteType = request.NoteType,
            DateOfService = request.DateOfService,
            TotalTimedMinutes = request.TotalMinutes,
            CptEntries = cptEntries
        }, ct);

        var response = new NoteOperationResponse();
        response.ApplyValidation(validation);
        if (!validation.IsValid)
        {
            return response;
        }

        var clinicId = tenantContext.GetCurrentClinicId();
        var userId = identityContext.GetCurrentUserId();
        var now = DateTime.UtcNow;

        var note = new ClinicalNote
        {
            PatientId = request.PatientId,
            AppointmentId = request.AppointmentId,
            NoteType = request.NoteType,
            IsReEvaluation = request.IsReEvaluation,
            ContentJson = string.IsNullOrWhiteSpace(request.ContentJson) ? "{}" : request.ContentJson,
            DateOfService = request.DateOfService,
            CptCodesJson = string.IsNullOrWhiteSpace(request.CptCodesJson) ? "[]" : request.CptCodesJson,
            TherapistNpi = request.TherapistNpi?.Trim(),
            TotalTreatmentMinutes = ResolveTotalTreatmentMinutes(request.TotalMinutes, cptEntries),
            NoteStatus = NoteStatus.Draft,
            ClinicId = clinicId,
            LastModifiedUtc = now,
            ModifiedByUserId = userId,
            SyncState = SyncState.Pending
        };

        db.ClinicalNotes.Add(note);

        if (request.NoteType == NoteType.Evaluation)
        {
            var draftIntake = await db.IntakeForms
                .Where(form => form.PatientId == request.PatientId && !form.IsLocked)
                .OrderByDescending(form => form.LastModifiedUtc)
                .FirstOrDefaultAsync(ct);

            if (draftIntake is not null)
            {
                draftIntake.IsLocked = true;
                draftIntake.LastModifiedUtc = now;
                draftIntake.ModifiedByUserId = userId;
                draftIntake.SyncState = SyncState.Pending;
                await syncEngine.EnqueueAsync("IntakeForm", draftIntake.Id, SyncOperation.Update, ct);
            }
        }

        await db.SaveChangesAsync(ct);
        await syncEngine.EnqueueAsync("ClinicalNote", note.Id, SyncOperation.Create, ct);

        response.Note = ToResponse(note);
        return response;
    }

    public async Task<NoteOperationResponse> UpdateAsync(ClinicalNote note, UpdateNoteRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(note);
        ArgumentNullException.ThrowIfNull(request);

        if (request.TotalMinutes < 0)
        {
            throw new ArgumentException("TotalMinutes must be zero or greater.", nameof(request));
        }

        var effectiveCptCodesJson = request.CptCodesJson ?? note.CptCodesJson;
        var cptEntries = TryDeserializeCptCodes(effectiveCptCodesJson);
        if (cptEntries is null)
        {
            throw new ArgumentException("CptCodesJson is not valid JSON.", nameof(request));
        }

        var validation = await validationService.ValidateAsync(new NoteSaveComplianceRequest
        {
            PatientId = note.PatientId,
            ExistingNoteId = note.Id,
            NoteType = note.NoteType,
            DateOfService = request.DateOfService ?? note.DateOfService,
            TotalTimedMinutes = request.TotalMinutes ?? note.TotalTreatmentMinutes,
            CptEntries = cptEntries
        }, ct);

        var response = new NoteOperationResponse();
        response.ApplyValidation(validation);
        if (!validation.IsValid)
        {
            return response;
        }

        if (request.ContentJson is not null)
        {
            note.ContentJson = request.ContentJson;
        }

        if (request.DateOfService is not null)
        {
            note.DateOfService = request.DateOfService.Value;
        }

        if (request.CptCodesJson is not null)
        {
            note.CptCodesJson = request.CptCodesJson;
        }

        note.TotalTreatmentMinutes = request.TotalMinutes.HasValue || request.CptCodesJson is not null
            ? ResolveTotalTreatmentMinutes(request.TotalMinutes, cptEntries)
            : note.TotalTreatmentMinutes;

        var userId = identityContext.GetCurrentUserId();
        note.LastModifiedUtc = DateTime.UtcNow;
        note.ModifiedByUserId = userId;
        note.SyncState = SyncState.Pending;

        await db.SaveChangesAsync(ct);
        await syncEngine.EnqueueAsync("ClinicalNote", note.Id, SyncOperation.Update, ct);
        await auditService.LogNoteEditedAsync(AuditEvent.NoteEdited(note.Id, userId), ct);

        response.Note = ToResponse(note);
        return response;
    }

    internal static List<CptCodeEntry>? TryDeserializeCptCodes(string? cptCodesJson)
    {
        if (string.IsNullOrWhiteSpace(cptCodesJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<CptCodeEntry>>(cptCodesJson, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int? ResolveTotalTreatmentMinutes(int? explicitTotalMinutes, IReadOnlyCollection<CptCodeEntry> cptEntries)
    {
        if (explicitTotalMinutes.HasValue)
        {
            return explicitTotalMinutes.Value;
        }

        var aggregateMinutes = cptEntries
            .Where(entry => entry.Minutes.GetValueOrDefault() > 0)
            .Sum(entry => entry.Minutes!.Value);

        return aggregateMinutes > 0 ? aggregateMinutes : null;
    }

    private static NoteResponse ToResponse(ClinicalNote note) => new()
    {
        Id = note.Id,
        PatientId = note.PatientId,
        AppointmentId = note.AppointmentId,
        NoteType = note.NoteType,
        IsReEvaluation = note.IsReEvaluation,
        NoteStatus = note.NoteStatus,
        ContentJson = note.ContentJson,
        DateOfService = note.DateOfService,
        SignatureHash = note.SignatureHash,
        SignedUtc = note.SignedUtc,
        SignedByUserId = note.SignedByUserId,
        CptCodesJson = note.CptCodesJson,
        TherapistNpi = note.TherapistNpi,
        TotalTreatmentMinutes = note.TotalTreatmentMinutes,
        ClinicId = note.ClinicId,
        LastModifiedUtc = note.LastModifiedUtc,
        ObjectiveMetrics = note.ObjectiveMetrics.Select(metric => new ObjectiveMetricResponse
        {
            Id = metric.Id,
            NoteId = metric.NoteId,
            BodyPart = metric.BodyPart,
            MetricType = metric.MetricType,
            Value = metric.Value,
            Side = metric.Side,
            Unit = metric.Unit,
            IsWNL = metric.IsWNL,
            LastModifiedUtc = metric.LastModifiedUtc
        }).ToList()
    };
}

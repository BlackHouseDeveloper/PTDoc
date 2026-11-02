using System.Data;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Identity;
using PTDoc.Application.Services;
using PTDoc.Application.Settings;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Infrastructure.Services;

public sealed class NoteHandoffService(ApplicationDbContext db, IIdentityContextAccessor identity,
    ITenantContextAccessor tenant, IPermissionEvaluator permissions) : INoteHandoffService
{
    public async Task<NoteHandoffResult<NoteHandoffContext>> GetAsync(Guid noteId, CancellationToken cancellationToken = default)
    {
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null) return new(NoteHandoffStatus.Forbidden);
        var clinicId = actor.ClinicId!.Value;
        var note = await GetNoteAsync(noteId, clinicId, cancellationToken);
        if (note is null) return new(NoteHandoffStatus.NotFound);
        var users = await db.Users.AsNoTracking().Where(u => u.ClinicId == clinicId && u.IsActive)
            .OrderBy(u => u.LastName).ThenBy(u => u.FirstName).ThenBy(u => u.Id).ToListAsync(cancellationToken);
        var recipients = new List<NoteHandoffClinician>();
        // Permission is role-scoped; evaluate once for the one permitted opposite role.
        var oppositeRole = OppositeRole(actor.Role);
        if (await CanReadAsync(clinicId, oppositeRole, cancellationToken))
            recipients.AddRange(users.Where(u => RolePermissionCatalog.NormalizeRole(u.Role) == oppositeRole).Select(ToClinician));
        var responsibility = await Events(noteId, clinicId).Where(e => e.Kind == NoteHandoffKind.Reassignment)
            .OrderByDescending(e => e.Sequence).Select(e => (Guid?)e.RecipientUserId).FirstOrDefaultAsync(cancellationToken);
        var responsible = responsibility is null ? null : await db.Users.AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == responsibility && u.ClinicId == clinicId, cancellationToken);
        return new(NoteHandoffStatus.Succeeded, new(noteId, Utc(note.LastModifiedUtc),
            await SequenceAsync(noteId, clinicId, cancellationToken), responsible is not null ? ToClinician(responsible)
                : responsibility.HasValue ? new(responsibility.Value, "Previously assigned clinician (no longer in this clinic)", "") : null, recipients));
    }

    public async Task<NoteHandoffResult<NoteHandoffReceipt>> SendAsync(Guid noteId, SendNoteHandoffRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.RequestId == Guid.Empty || request.RecipientUserId == Guid.Empty || !Enum.IsDefined(request.Kind)
            || request.ExpectedSequence < 0 || request.ExpectedSequence == long.MaxValue
            || request.ExpectedNoteVersionUtc.Kind != DateTimeKind.Utc || request.ExpectedNoteVersionUtc == default)
            return new(NoteHandoffStatus.Invalid);

        // Provider retry strategies can repeat a transaction after an uncertain commit. The command ID
        // and payload check make the entire retry safe, including the in-app notification.
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            var actor = await GetActorAsync(cancellationToken);
            if (actor is null) return new NoteHandoffResult<NoteHandoffReceipt>(NoteHandoffStatus.Forbidden);
            var clinicId = actor.ClinicId!.Value;
            try { return await SendOnceAsync(noteId, clinicId, actor, request, cancellationToken); }
            catch (DbUpdateException)
            {
                var replay = await FindReplayAsync(noteId, clinicId, actor.Id, request, cancellationToken);
                if (replay is not null) return replay;
                if (await SequenceAsync(noteId, clinicId, cancellationToken) != request.ExpectedSequence)
                    return new(NoteHandoffStatus.Conflict);
                throw; // Do not disguise unavailable storage or unrelated integrity failures as a stale form.
            }
        });
    }

    private async Task<NoteHandoffResult<NoteHandoffReceipt>> SendOnceAsync(Guid noteId, Guid clinicId, User actor,
        SendNoteHandoffRequest request, CancellationToken ct)
    {
        // Serializes the note-version read against clinical edits without updating the clinical row.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var note = await GetNoteAsync(noteId, clinicId, ct);
        if (note is null) return new(NoteHandoffStatus.NotFound);
        var replay = await FindReplayAsync(noteId, clinicId, actor.Id, request, ct);
        if (replay is not null) return replay;
        var recipient = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == request.RecipientUserId
            && u.ClinicId == clinicId && u.IsActive, ct);
        if (recipient is null || RolePermissionCatalog.NormalizeRole(recipient.Role) != OppositeRole(actor.Role)
            || !await CanReadAsync(clinicId, recipient.Role, ct)) return new(NoteHandoffStatus.Forbidden);
        if (Utc(note.LastModifiedUtc) != request.ExpectedNoteVersionUtc
            || await SequenceAsync(noteId, clinicId, ct) != request.ExpectedSequence)
            return new(NoteHandoffStatus.Conflict);

        var handoff = new NoteHandoff
        {
            Id = request.RequestId,
            ClinicId = clinicId,
            ClinicalNoteId = noteId,
            Sequence = request.ExpectedSequence + 1,
            SenderUserId = actor.Id,
            RecipientUserId = recipient.Id,
            Kind = request.Kind,
            NoteVersionUtc = request.ExpectedNoteVersionUtc,
            CreatedUtc = DateTime.UtcNow
        };
        var notification = new UserNotification
        {
            Id = request.RequestId,
            ClinicId = clinicId,
            UserId = recipient.Id,
            Type = "note",
            Title = request.Kind == NoteHandoffKind.Collaboration ? "Note collaboration" : "Note reassigned",
            Message = request.Kind == NoteHandoffKind.Collaboration
                ? "A clinician sent you a note for collaboration. Existing edit and signing permissions apply."
                : "A clinician assigned you responsibility for a note. Existing edit and signing permissions apply.",
            TargetUrl = $"/patient/{note.PatientId}/notes/{note.Id}",
            Timestamp = new DateTimeOffset(handoff.CreatedUtc)
        };
        try
        {
            db.NoteHandoffs.Add(handoff);
            db.UserNotifications.Add(notification);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Success(handoff);
        }
        finally
        {
            // A provider retry must start with fresh tracked entities even after an uncertain commit.
            db.Entry(handoff).State = EntityState.Detached;
            db.Entry(notification).State = EntityState.Detached;
        }
    }

    private async Task<User?> GetActorAsync(CancellationToken ct)
    {
        var clinicId = tenant.GetCurrentClinicId();
        var userId = identity.TryGetCurrentUserId();
        if (clinicId is null || userId is null) return null;
        var actor = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId && u.ClinicId == clinicId && u.IsActive, ct);
        if (actor is null || RolePermissionCatalog.NormalizeRole(actor.Role) is not (Roles.PT or Roles.PTA)) return null;
        var write = await permissions.EvaluateAsync(clinicId.Value, actor.Role,
            CapabilityKey.ClinicalNotesCreate, PermissionLevel.Edit, true, ct);
        return write.EffectiveAllowed && await CanReadAsync(clinicId.Value, actor.Role, ct) ? actor : null;
    }

    private async Task<bool> CanReadAsync(Guid clinicId, string role, CancellationToken ct) =>
        (await permissions.EvaluateAsync(clinicId, role, CapabilityKey.ClinicalNotesView, PermissionLevel.View, true, ct)).EffectiveAllowed;
    private Task<ClinicalNote?> GetNoteAsync(Guid noteId, Guid clinicId, CancellationToken ct) =>
        db.ClinicalNotes.AsNoTracking().SingleOrDefaultAsync(n => n.Id == noteId && n.ClinicId == clinicId
            && n.Patient != null && n.Patient.ClinicId == clinicId, ct);
    private IQueryable<NoteHandoff> Events(Guid noteId, Guid clinicId) => db.NoteHandoffs.AsNoTracking()
        .Where(e => e.ClinicalNoteId == noteId && e.ClinicId == clinicId);
    private async Task<long> SequenceAsync(Guid noteId, Guid clinicId, CancellationToken ct) =>
        await Events(noteId, clinicId).MaxAsync(e => (long?)e.Sequence, ct) ?? 0;
    private async Task<NoteHandoffResult<NoteHandoffReceipt>?> FindReplayAsync(Guid noteId, Guid clinicId, Guid actorId,
        SendNoteHandoffRequest request, CancellationToken ct)
    {
        var existing = await db.NoteHandoffs.AsNoTracking().SingleOrDefaultAsync(e => e.Id == request.RequestId && e.ClinicId == clinicId, ct);
        if (existing is null) return null;
        return existing.ClinicalNoteId == noteId && existing.SenderUserId == actorId
            && existing.RecipientUserId == request.RecipientUserId && existing.Kind == request.Kind
            && Utc(existing.NoteVersionUtc) == request.ExpectedNoteVersionUtc && existing.Sequence == request.ExpectedSequence + 1
            ? Success(existing) : new(NoteHandoffStatus.Conflict);
    }
    private static NoteHandoffResult<NoteHandoffReceipt> Success(NoteHandoff e) =>
        new(NoteHandoffStatus.Succeeded, new(e.Id, e.Sequence, e.RecipientUserId, e.Kind));
    private static NoteHandoffClinician ToClinician(User u) => new(u.Id, $"{u.FirstName} {u.LastName}".Trim(), RolePermissionCatalog.NormalizeRole(u.Role));
    private static string OppositeRole(string role) => RolePermissionCatalog.NormalizeRole(role) == Roles.PT ? Roles.PTA : Roles.PT;
    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}

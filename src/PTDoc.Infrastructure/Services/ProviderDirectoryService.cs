using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Compliance;
using PTDoc.Application.Identity;
using PTDoc.Application.Providers;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Infrastructure.Services;

public sealed class ProviderDirectoryService(
    ApplicationDbContext db,
    ITenantContextAccessor tenantContext,
    IIdentityContextAccessor identityContext,
    IAuditService auditService) : IProviderDirectoryService
{
    public async Task<IReadOnlyList<ProviderDirectoryEntryDto>> SearchAsync(string? query, ProviderDirectoryStatus? status, int take, CancellationToken cancellationToken)
        => await SearchCoreAsync(query, ProviderDirectoryStatus.Active, take, includeDuplicateCandidates: false, cancellationToken);

    public async Task<IReadOnlyList<ProviderDirectoryEntryDto>> SearchForAdministrationAsync(string? query, ProviderDirectoryStatus? status, int take, CancellationToken cancellationToken)
        => await SearchCoreAsync(query, status, take, includeDuplicateCandidates: status == ProviderDirectoryStatus.Pending, cancellationToken);

    private async Task<IReadOnlyList<ProviderDirectoryEntryDto>> SearchCoreAsync(string? query, ProviderDirectoryStatus? status, int take, bool includeDuplicateCandidates, CancellationToken cancellationToken)
    {
        var normalizedTake = Math.Clamp(take, 1, 100);
        var source = db.ProviderDirectoryEntries.AsNoTracking().Where(e => !e.IsArchived);
        if (status.HasValue) source = source.Where(e => e.Status == status.Value);
        else source = source.Where(e => e.Status == ProviderDirectoryStatus.Active);
        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim();
            source = source.Where(e => e.FirstName.Contains(term) || e.LastName.Contains(term) ||
                (e.Npi != null && e.Npi.Contains(term)) || (e.OrganizationName != null && e.OrganizationName.Contains(term)));
        }

        var rows = await source.OrderBy(e => e.LastName).ThenBy(e => e.FirstName).Take(normalizedTake).ToListAsync(cancellationToken);
        if (includeDuplicateCandidates)
        {
            var pending = new List<ProviderDirectoryEntryDto>(rows.Count);
            foreach (var row in rows) pending.Add(await MapWithDuplicatesAsync(row, cancellationToken));
            return pending;
        }
        return rows.Select(Map).ToList();
    }

    public async Task<ProviderDirectoryEntryDto?> GetAsync(Guid providerId, CancellationToken cancellationToken)
    {
        var entity = await db.ProviderDirectoryEntries.AsNoTracking().FirstOrDefaultAsync(
            e => e.Id == providerId && !e.IsArchived && e.Status == ProviderDirectoryStatus.Active,
            cancellationToken);
        return entity is null ? null : Map(entity);
    }

    public async Task<ProviderDirectoryEntryDto> SubmitAsync(SubmitProviderCandidateRequest request, CancellationToken cancellationToken)
    {
        Validate(request);
        var clinicId = RequireClinic();
        var userId = identityContext.GetCurrentUserId();
        var npi = NormalizeNpi(request.Npi);
        if (npi is not null && await db.ProviderDirectoryEntries.AnyAsync(e => e.Npi == npi && !e.IsArchived && e.Status == ProviderDirectoryStatus.Active, cancellationToken))
            throw new InvalidOperationException("A provider with this NPI already exists in the clinic directory.");

        if (request.PatientId.HasValue && !await db.Patients.AnyAsync(p => p.Id == request.PatientId.Value, cancellationToken))
            throw new KeyNotFoundException("Patient not found in the current clinic.");

        var now = DateTime.UtcNow;
        var entity = new ProviderDirectoryEntry
        {
            ClinicId = clinicId,
            Status = ProviderDirectoryStatus.Pending,
            SubmissionSource = request.SubmissionSource,
            SubmittedByUserId = request.SubmissionSource == ProviderSubmissionSource.PatientIntake ? null : userId,
            SubmittedAtUtc = now,
            LastModifiedUtc = now,
            ModifiedByUserId = userId,
            SyncState = SyncState.Pending
        };
        Apply(entity, request);
        db.ProviderDirectoryEntries.Add(entity);

        if (request.PatientId.HasValue && request.PatientRole.HasValue)
        {
            db.PatientProviderRelationships.Add(new PatientProviderRelationship
            {
                PatientId = request.PatientId.Value,
                ProviderDirectoryEntryId = entity.Id,
                ClinicId = clinicId,
                Role = request.PatientRole.Value,
                IsPrimary = true,
                LastModifiedUtc = now,
                ModifiedByUserId = userId,
                SyncState = SyncState.Pending
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        await AuditAsync("ProviderCandidateSubmitted", entity.Id, userId, true, cancellationToken);
        return await MapWithDuplicatesAsync(entity, cancellationToken);
    }

    public async Task<ProviderDirectoryEntryDto> UpdateAsync(Guid providerId, UpdateProviderCandidateRequest request, CancellationToken cancellationToken)
    {
        Validate(request);
        var entity = await db.ProviderDirectoryEntries.FirstOrDefaultAsync(e => e.Id == providerId, cancellationToken)
            ?? throw new KeyNotFoundException("Provider candidate not found.");
        if (entity.Status != ProviderDirectoryStatus.Pending) throw new InvalidOperationException("Only pending provider candidates can be edited.");
        if (entity.LastModifiedUtc != request.ExpectedLastModifiedUtc) throw new DbUpdateConcurrencyException("The provider candidate changed in another session.");
        var npi = NormalizeNpi(request.Npi);
        if (npi is not null && await db.ProviderDirectoryEntries.AnyAsync(e => e.Id != providerId && e.Npi == npi && !e.IsArchived && e.Status == ProviderDirectoryStatus.Active, cancellationToken))
            throw new InvalidOperationException("A provider with this NPI already exists in the clinic directory.");
        Apply(entity, request);
        Touch(entity);
        await db.SaveChangesAsync(cancellationToken);
        return await MapWithDuplicatesAsync(entity, cancellationToken);
    }

    public async Task<ProviderDirectoryEntryDto> ApproveAsync(Guid providerId, ProviderDecisionRequest request, CancellationToken cancellationToken)
    {
        var entity = await db.ProviderDirectoryEntries.FirstOrDefaultAsync(e => e.Id == providerId, cancellationToken)
            ?? throw new KeyNotFoundException("Provider candidate not found.");
        if (entity.Status != ProviderDirectoryStatus.Pending) throw new InvalidOperationException("Only pending provider candidates can be approved.");

        var possibleDuplicates = await FindPossibleDuplicatesAsync(entity, cancellationToken);
        var exactActiveNpiDuplicate = entity.Npi is not null && possibleDuplicates.Any(candidate =>
            candidate.Status == ProviderDirectoryStatus.Active && candidate.Npi == entity.Npi);
        if (exactActiveNpiDuplicate && !request.MergeIntoProviderId.HasValue)
            throw new InvalidOperationException("An active provider already uses this NPI. The candidate must be merged into that directory entry.");
        if (possibleDuplicates.Count > 0 && !request.MergeIntoProviderId.HasValue && !request.ConfirmDuplicate)
            throw new InvalidOperationException("Possible duplicate providers were found. Merge the candidate or explicitly confirm that it is a separate provider.");

        var userId = identityContext.GetCurrentUserId();
        if (request.MergeIntoProviderId.HasValue)
        {
            var target = await db.ProviderDirectoryEntries.FirstOrDefaultAsync(e => e.Id == request.MergeIntoProviderId.Value && e.Status == ProviderDirectoryStatus.Active, cancellationToken)
                ?? throw new KeyNotFoundException("Active merge target not found.");
            var relationships = await db.PatientProviderRelationships.Where(r => r.ProviderDirectoryEntryId == entity.Id && !r.IsArchived).ToListAsync(cancellationToken);
            foreach (var relationship in relationships)
            {
                var duplicate = await db.PatientProviderRelationships.AnyAsync(r => r.PatientId == relationship.PatientId && r.ProviderDirectoryEntryId == target.Id && r.Role == relationship.Role && !r.IsArchived, cancellationToken);
                if (duplicate) relationship.IsArchived = true;
                else relationship.ProviderDirectoryEntryId = target.Id;
                relationship.LastModifiedUtc = DateTime.UtcNow;
                relationship.ModifiedByUserId = userId;
                relationship.SyncState = SyncState.Pending;
            }
            entity.Status = ProviderDirectoryStatus.Archived;
            entity.IsArchived = true;
        }
        else
        {
            entity.Status = ProviderDirectoryStatus.Active;
        }
        entity.ReviewReason = Trim(request.Reason);
        entity.ReviewedAtUtc = DateTime.UtcNow;
        entity.ReviewedByUserId = userId;
        Touch(entity);
        await db.SaveChangesAsync(cancellationToken);
        await AuditAsync(request.MergeIntoProviderId.HasValue ? "ProviderCandidateMerged" : "ProviderCandidateApproved", entity.Id, userId, true, cancellationToken);
        return Map(entity);
    }

    public async Task<ProviderDirectoryEntryDto> RejectAsync(Guid providerId, ProviderDecisionRequest request, CancellationToken cancellationToken)
    {
        var entity = await db.ProviderDirectoryEntries.FirstOrDefaultAsync(e => e.Id == providerId, cancellationToken)
            ?? throw new KeyNotFoundException("Provider candidate not found.");
        if (entity.Status != ProviderDirectoryStatus.Pending) throw new InvalidOperationException("Only pending provider candidates can be rejected.");
        var userId = identityContext.GetCurrentUserId();
        entity.Status = ProviderDirectoryStatus.Rejected;
        entity.ReviewReason = Trim(request.Reason);
        entity.ReviewedAtUtc = DateTime.UtcNow;
        entity.ReviewedByUserId = userId;
        Touch(entity);
        await db.SaveChangesAsync(cancellationToken);
        await AuditAsync("ProviderCandidateRejected", entity.Id, userId, true, cancellationToken);
        return Map(entity);
    }

    public async Task ArchiveAsync(Guid providerId, ProviderDecisionRequest request, CancellationToken cancellationToken)
    {
        var entity = await db.ProviderDirectoryEntries.FirstOrDefaultAsync(e => e.Id == providerId, cancellationToken)
            ?? throw new KeyNotFoundException("Provider not found.");
        var userId = identityContext.GetCurrentUserId();
        entity.Status = ProviderDirectoryStatus.Archived;
        entity.IsArchived = true;
        entity.ReviewReason = Trim(request.Reason);
        entity.ReviewedAtUtc = DateTime.UtcNow;
        entity.ReviewedByUserId = userId;
        Touch(entity);
        await db.SaveChangesAsync(cancellationToken);
        await AuditAsync("ProviderArchived", entity.Id, userId, true, cancellationToken);
    }

    public async Task<IReadOnlyList<PatientProviderRelationshipDto>> ListPatientRelationshipsAsync(Guid patientId, CancellationToken cancellationToken)
    {
        if (!await db.Patients.AnyAsync(p => p.Id == patientId, cancellationToken)) throw new KeyNotFoundException("Patient not found.");
        var rows = await db.PatientProviderRelationships.AsNoTracking().Include(r => r.Provider)
            .Where(r => r.PatientId == patientId && !r.IsArchived)
            .OrderBy(r => r.Role).ThenByDescending(r => r.IsPrimary).ToListAsync(cancellationToken);
        return rows.Select(Map).ToList();
    }

    public async Task<PatientProviderRelationshipDto> UpsertPatientRelationshipAsync(Guid patientId, Guid? relationshipId, UpsertPatientProviderRelationshipRequest request, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(request.Role))
            throw new ArgumentException("Provider relationship role must be a supported value.");
        if (request.EffectiveStartDate.HasValue
            && request.EffectiveEndDate.HasValue
            && request.EffectiveStartDate > request.EffectiveEndDate)
            throw new ArgumentException("Provider relationship start date must not be after the end date.");
        var patient = await db.Patients.FirstOrDefaultAsync(p => p.Id == patientId, cancellationToken) ?? throw new KeyNotFoundException("Patient not found.");
        var provider = await db.ProviderDirectoryEntries.FirstOrDefaultAsync(e => e.Id == request.ProviderId && !e.IsArchived, cancellationToken) ?? throw new KeyNotFoundException("Provider not found.");
        if (provider.Status != ProviderDirectoryStatus.Active && !await db.PatientProviderRelationships.AnyAsync(r => r.PatientId == patientId && r.ProviderDirectoryEntryId == provider.Id, cancellationToken))
            throw new InvalidOperationException("Pending providers can only remain linked to the patient for whom they were submitted.");
        var userId = identityContext.GetCurrentUserId();
        var relationship = relationshipId.HasValue
            ? await db.PatientProviderRelationships.FirstOrDefaultAsync(r => r.Id == relationshipId.Value && r.PatientId == patientId, cancellationToken) ?? throw new KeyNotFoundException("Provider relationship not found.")
            : new PatientProviderRelationship { PatientId = patient.Id, ClinicId = patient.ClinicId };
        if (request.ExpectedLastModifiedUtc.HasValue && relationshipId.HasValue && relationship.LastModifiedUtc != request.ExpectedLastModifiedUtc.Value)
            throw new DbUpdateConcurrencyException("The provider relationship changed in another session.");
        relationship.ProviderDirectoryEntryId = provider.Id;
        relationship.Role = request.Role;
        relationship.IsPrimary = request.IsPrimary;
        relationship.EffectiveStartDate = request.EffectiveStartDate;
        relationship.EffectiveEndDate = request.EffectiveEndDate;
        relationship.IsArchived = false;
        relationship.LastModifiedUtc = DateTime.UtcNow;
        relationship.ModifiedByUserId = userId;
        relationship.SyncState = SyncState.Pending;
        if (request.IsPrimary)
        {
            var peers = await db.PatientProviderRelationships.Where(r => r.PatientId == patientId && r.Role == request.Role && r.Id != relationship.Id && r.IsPrimary && !r.IsArchived).ToListAsync(cancellationToken);
            foreach (var peer in peers) { peer.IsPrimary = false; peer.LastModifiedUtc = DateTime.UtcNow; peer.ModifiedByUserId = userId; peer.SyncState = SyncState.Pending; }
        }
        if (!relationshipId.HasValue) db.PatientProviderRelationships.Add(relationship);
        await db.SaveChangesAsync(cancellationToken);
        relationship.Provider = provider;
        return Map(relationship);
    }

    public async Task ArchivePatientRelationshipAsync(Guid patientId, Guid relationshipId, CancellationToken cancellationToken)
    {
        var relationship = await db.PatientProviderRelationships.FirstOrDefaultAsync(r => r.Id == relationshipId && r.PatientId == patientId, cancellationToken) ?? throw new KeyNotFoundException("Provider relationship not found.");
        relationship.IsArchived = true;
        relationship.LastModifiedUtc = DateTime.UtcNow;
        relationship.ModifiedByUserId = identityContext.GetCurrentUserId();
        relationship.SyncState = SyncState.Pending;
        await db.SaveChangesAsync(cancellationToken);
    }

    private Guid RequireClinic() => tenantContext.GetCurrentClinicId() ?? throw new InvalidOperationException("A clinic context is required.");
    private void Touch(ProviderDirectoryEntry entity) { entity.LastModifiedUtc = DateTime.UtcNow; entity.ModifiedByUserId = identityContext.GetCurrentUserId(); entity.SyncState = SyncState.Pending; }
    private static string? NormalizeNpi(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static void Validate(SubmitProviderCandidateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName))
            throw new ArgumentException("Provider first and last name are required.");
        var npi = NormalizeNpi(request.Npi);
        if (npi is not null && (npi.Length != 10 || npi.Any(c => !char.IsDigit(c))))
            throw new ArgumentException("NPI must contain exactly 10 digits.");
        if (request.PatientRole.HasValue && !request.PatientId.HasValue)
            throw new ArgumentException("A patient role requires a patient identifier.");
        if (!Enum.IsDefined(request.SubmissionSource)
            || request.PatientRole.HasValue && !Enum.IsDefined(request.PatientRole.Value))
            throw new ArgumentException("Provider submission source and patient role must be supported values.");
    }
    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static void Apply(ProviderDirectoryEntry entity, SubmitProviderCandidateRequest request)
    {
        entity.FirstName = request.FirstName.Trim(); entity.LastName = request.LastName.Trim(); entity.Credentials = Trim(request.Credentials);
        entity.Npi = NormalizeNpi(request.Npi); entity.Specialty = Trim(request.Specialty); entity.TaxonomyCode = Trim(request.TaxonomyCode);
        entity.OrganizationName = Trim(request.OrganizationName); entity.Phone = Trim(request.Phone); entity.Fax = Trim(request.Fax); entity.Email = Trim(request.Email);
        entity.AddressLine1 = Trim(request.AddressLine1); entity.AddressLine2 = Trim(request.AddressLine2); entity.City = Trim(request.City); entity.State = Trim(request.State); entity.ZipCode = Trim(request.ZipCode);
    }
    private async Task<ProviderDirectoryEntryDto> MapWithDuplicatesAsync(ProviderDirectoryEntry entity, CancellationToken cancellationToken)
    {
        var dto = Map(entity);
        var matches = await FindPossibleDuplicatesAsync(entity, cancellationToken);
        dto.PossibleDuplicates = matches.Select(e => new ProviderDuplicateCandidateDto { Id = e.Id, DisplayName = DisplayName(e), Npi = e.Npi, Reason = entity.Npi != null && e.Npi == entity.Npi ? "Exact NPI match" : "Matching name and contact details", Status=e.Status }).ToList();
        return dto;
    }
    private async Task<List<ProviderDirectoryEntry>> FindPossibleDuplicatesAsync(ProviderDirectoryEntry entity, CancellationToken cancellationToken)
    {
        var first = entity.FirstName.ToUpper(); var last = entity.LastName.ToUpper();
        var phone = NormalizeComparable(entity.Phone); var address = NormalizeComparable(entity.AddressLine1);
        var candidates = await db.ProviderDirectoryEntries.AsNoTracking().Where(e => e.Id != entity.Id && !e.IsArchived &&
            ((entity.Npi != null && e.Npi == entity.Npi) || (e.FirstName.ToUpper() == first && e.LastName.ToUpper() == last)))
            .Take(25).ToListAsync(cancellationToken);
        return candidates.Where(e => entity.Npi != null && e.Npi == entity.Npi ||
            phone is not null && NormalizeComparable(e.Phone) == phone ||
            address is not null && NormalizeComparable(e.AddressLine1) == address).Take(10).ToList();
    }
    private static string? NormalizeComparable(string? value) => string.IsNullOrWhiteSpace(value)
        ? null
        : new string(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
    private static ProviderDirectoryEntryDto Map(ProviderDirectoryEntry e) => new() { Id=e.Id, FirstName=e.FirstName, LastName=e.LastName, DisplayName=DisplayName(e), Credentials=e.Credentials, Npi=e.Npi, Specialty=e.Specialty, TaxonomyCode=e.TaxonomyCode, OrganizationName=e.OrganizationName, Phone=e.Phone, Fax=e.Fax, Email=e.Email, AddressLine1=e.AddressLine1, AddressLine2=e.AddressLine2, City=e.City, State=e.State, ZipCode=e.ZipCode, Status=e.Status, SubmissionSource=e.SubmissionSource, LastModifiedUtc=e.LastModifiedUtc };
    private static PatientProviderRelationshipDto Map(PatientProviderRelationship r) => new() { Id=r.Id, PatientId=r.PatientId, ProviderId=r.ProviderDirectoryEntryId, Role=r.Role, IsPrimary=r.IsPrimary, EffectiveStartDate=r.EffectiveStartDate, EffectiveEndDate=r.EffectiveEndDate, IsArchived=r.IsArchived, LastModifiedUtc=r.LastModifiedUtc, Provider=r.Provider is null ? new() : Map(r.Provider) };
    private static string DisplayName(ProviderDirectoryEntry e) => string.Join(" ", new[] { e.FirstName, e.LastName, e.Credentials }.Where(v => !string.IsNullOrWhiteSpace(v)));
    private Task AuditAsync(string eventType, Guid id, Guid userId, bool success, CancellationToken ct) => auditService.LogRuleEvaluationAsync(new AuditEvent { EventType=eventType, EntityType="ProviderDirectoryEntry", EntityId=id, UserId=userId, Success=success, Metadata=new() { ["Status"] = success ? "Succeeded" : "Failed" } }, ct);
}

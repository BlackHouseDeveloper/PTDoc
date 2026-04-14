using Microsoft.EntityFrameworkCore;
using PTDoc.Application.DTOs;
using PTDoc.Application.Identity;
using PTDoc.Application.Intake;
using PTDoc.Application.ReferenceData;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PTDoc.Infrastructure.Services;

/// <summary>
/// Database-backed implementation of IIntakeService.
/// Replaces MockIntakeService for production use.
/// Creates Patient and IntakeForm records in ApplicationDbContext.
/// </summary>
public sealed class IntakeService : IIntakeService
{
    private readonly ApplicationDbContext _context;
    private readonly ITenantContextAccessor _tenantContext;
    private readonly IIdentityContextAccessor _identityContext;
    private readonly IIntakeReferenceDataCatalogService _intakeReferenceData;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public IntakeService(
        ApplicationDbContext context,
        ITenantContextAccessor tenantContext,
        IIdentityContextAccessor identityContext,
        IIntakeReferenceDataCatalogService intakeReferenceData)
    {
        _context = context;
        _tenantContext = tenantContext;
        _identityContext = identityContext;
        _intakeReferenceData = intakeReferenceData;
    }

    public async Task<IntakeResponseDraft?> GetDraftByPatientIdAsync(Guid patientId, CancellationToken cancellationToken = default)
    {
        var intake = await _context.IntakeForms
            .AsNoTracking()
            .Where(f => f.PatientId == patientId && !f.IsLocked)
            .OrderByDescending(f => f.LastModifiedUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (intake is null)
            return null;

        return DeserializeDraft(intake);
    }

    public async Task<IntakeEnsureDraftResult> EnsureDraftAsync(
        Guid patientId,
        IntakeResponseDraft? seedState = null,
        CancellationToken cancellationToken = default)
    {
        var patientExists = await _context.Patients
            .AsNoTracking()
            .AnyAsync(patient => patient.Id == patientId, cancellationToken);

        if (!patientExists)
        {
            return IntakeEnsureDraftResult.NotFound($"Patient {patientId} not found.");
        }

        var existingDraft = await _context.IntakeForms
            .AsNoTracking()
            .Where(form => form.PatientId == patientId && !form.IsLocked)
            .OrderByDescending(form => form.LastModifiedUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingDraft is not null)
        {
            return IntakeEnsureDraftResult.Existing(DeserializeDraft(existingDraft));
        }

        var hasLockedIntake = await _context.IntakeForms
            .AsNoTracking()
            .AnyAsync(form => form.PatientId == patientId && form.IsLocked, cancellationToken);

        if (hasLockedIntake)
        {
            return IntakeEnsureDraftResult.Locked("Intake is locked for this patient and a new draft cannot be created.");
        }

        var userId = _identityContext.GetCurrentUserId();
        var clinicId = _tenantContext.GetCurrentClinicId();
        var draft = Clone(seedState ?? new IntakeResponseDraft());
        draft.PatientId = patientId;
        draft.IsLocked = false;
        draft.IsSubmitted = false;

        var intake = new IntakeForm
        {
            PatientId = patientId,
            TemplateVersion = "1.0",
            AccessToken = GenerateAccessTokenPlaceholderHash(),
            ResponseJson = SerializeDraft(draft),
            StructuredDataJson = SerializeStructuredData(draft.StructuredData),
            PainMapData = BuildPainMapJson(draft),
            Consents = BuildConsentsJson(draft),
            IsLocked = false,
            ClinicId = clinicId,
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = userId,
            SyncState = SyncState.Pending
        };

        _context.IntakeForms.Add(intake);
        await _context.SaveChangesAsync(cancellationToken);

        draft.IntakeId = intake.Id;
        return IntakeEnsureDraftResult.Created(draft);
    }

    public async Task<IReadOnlyList<PatientListItemResponse>> SearchEligiblePatientsAsync(
        string? query = null,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        var normalizedTake = take <= 0 ? 100 : Math.Min(take, 250);
        var normalizedQuery = query?.Trim();

        var patientQuery = _context.Patients
            .AsNoTracking()
            .Where(patient => !patient.IsArchived)
            .Select(patient => new
            {
                Patient = patient,
                HasUnlockedDraft = _context.IntakeForms.Any(form => form.PatientId == patient.Id && !form.IsLocked),
                HasLockedIntake = _context.IntakeForms.Any(form => form.PatientId == patient.Id && form.IsLocked)
            })
            .Where(row => row.HasUnlockedDraft || !row.HasLockedIntake);

        if (!string.IsNullOrWhiteSpace(normalizedQuery))
        {
            var likePattern = $"%{normalizedQuery}%";
            patientQuery = patientQuery.Where(row =>
                EF.Functions.Like(row.Patient.FirstName + " " + row.Patient.LastName, likePattern) ||
                (row.Patient.MedicalRecordNumber != null && EF.Functions.Like(row.Patient.MedicalRecordNumber, likePattern)) ||
                (row.Patient.Email != null && EF.Functions.Like(row.Patient.Email, likePattern)));
        }

        return await patientQuery
            .OrderBy(row => row.Patient.LastName)
            .ThenBy(row => row.Patient.FirstName)
            .Take(normalizedTake)
            .Select(row => new PatientListItemResponse
            {
                Id = row.Patient.Id,
                DisplayName = row.Patient.FirstName + " " + row.Patient.LastName,
                FirstName = row.Patient.FirstName,
                LastName = row.Patient.LastName,
                MedicalRecordNumber = row.Patient.MedicalRecordNumber,
                Email = row.Patient.Email,
                Phone = row.Patient.Phone,
                DateOfBirth = row.Patient.DateOfBirth,
                IsArchived = row.Patient.IsArchived
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<Guid> CreateTemporaryPatientAndDraftIntakeAsync(IntakeResponseDraft state, CancellationToken cancellationToken = default)
    {
        var clinicId = _tenantContext.GetCurrentClinicId();
        var userId = _identityContext.GetCurrentUserId();

        // Parse name into first/last — split on first space only
        var name = state.FullName?.Trim() ?? string.Empty;
        var spaceIndex = name.IndexOf(' ');
        // Use everything before first space as first name, rest as last name
        var firstName = spaceIndex > 0 ? name[..spaceIndex].Trim() : name;
        var lastName = spaceIndex > 0 ? name[(spaceIndex + 1)..].Trim() : string.Empty;

        var patient = new Patient
        {
            FirstName = string.IsNullOrWhiteSpace(firstName) ? "Unknown" : firstName,
            LastName = string.IsNullOrWhiteSpace(lastName) ? "Patient" : lastName,
            DateOfBirth = state.DateOfBirth ?? DateTime.UtcNow.AddYears(-30),
            Email = state.EmailAddress?.Trim(),
            Phone = state.PhoneNumber?.Trim(),
            AddressLine1 = state.AddressLine1?.Trim(),
            AddressLine2 = state.AddressLine2?.Trim(),
            City = state.City?.Trim(),
            State = state.StateOrProvince?.Trim(),
            ZipCode = state.PostalCode?.Trim(),
            EmergencyContactName = state.EmergencyContactName?.Trim(),
            EmergencyContactPhone = state.EmergencyContactPhone?.Trim(),
            PayerInfoJson = BuildPayerInfoJson(state),
            ConsentSigned = state.HipaaAcknowledged,
            ConsentSignedDate = state.HipaaAcknowledged ? DateTime.UtcNow : null,
            ClinicId = clinicId,
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = userId,
            SyncState = SyncState.Pending
        };

        _context.Patients.Add(patient);

        var draft = new IntakeResponseDraft();
        // Copy state properties
        CopyDraftProperties(state, draft);
        draft.PatientId = patient.Id;

        var intake = new IntakeForm
        {
            PatientId = patient.Id,
            TemplateVersion = "1.0",
            // Canonical invite links are minted separately; store a non-shareable placeholder hash.
            AccessToken = GenerateAccessTokenPlaceholderHash(),
            ResponseJson = SerializeDraft(draft),
            StructuredDataJson = SerializeStructuredData(draft.StructuredData),
            PainMapData = BuildPainMapJson(draft),
            Consents = BuildConsentsJson(state),
            IsLocked = false,
            ClinicId = clinicId,
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = userId,
            SyncState = SyncState.Pending
        };

        _context.IntakeForms.Add(intake);
        await _context.SaveChangesAsync(cancellationToken);

        return patient.Id;
    }

    public async Task SaveDraftAsync(IntakeResponseDraft state, CancellationToken cancellationToken = default)
    {
        if (state.PatientId is null)
            return;

        var userId = _identityContext.GetCurrentUserId();

        var intake = await _context.IntakeForms
            .Where(f => f.PatientId == state.PatientId.Value && !f.IsLocked)
            .OrderByDescending(f => f.LastModifiedUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (intake is null)
        {
            // Before creating a new draft, check if a locked intake already exists for this patient.
            // A locked intake (created after Eval) must not be silently replaced with a new unlocked draft.
            var hasLockedIntake = await _context.IntakeForms
                .AnyAsync(f => f.PatientId == state.PatientId.Value && f.IsLocked, cancellationToken);

            if (hasLockedIntake)
                return;
            var clinicId = _tenantContext.GetCurrentClinicId();
            intake = new IntakeForm
            {
                PatientId = state.PatientId.Value,
                TemplateVersion = "1.0",
                // Canonical invite links are minted separately; store a non-shareable placeholder hash.
                AccessToken = GenerateAccessTokenPlaceholderHash(),
                ResponseJson = SerializeDraft(state),
                StructuredDataJson = SerializeStructuredData(state.StructuredData),
                PainMapData = BuildPainMapJson(state),
                Consents = BuildConsentsJson(state),
                IsLocked = false,
                ClinicId = clinicId,
                LastModifiedUtc = DateTime.UtcNow,
                ModifiedByUserId = userId,
                SyncState = SyncState.Pending
            };
            _context.IntakeForms.Add(intake);
        }
        else
        {
            intake.ResponseJson = SerializeDraft(state);
            intake.StructuredDataJson = SerializeStructuredData(state.StructuredData);
            intake.PainMapData = BuildPainMapJson(state);
            intake.Consents = BuildConsentsJson(state);
            intake.LastModifiedUtc = DateTime.UtcNow;
            intake.ModifiedByUserId = userId;
            intake.SyncState = SyncState.Pending;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task SubmitAsync(IntakeResponseDraft state, CancellationToken cancellationToken = default)
    {
        if (state.PatientId is null)
            return;

        var userId = _identityContext.GetCurrentUserId();

        var intake = await _context.IntakeForms
            .Where(f => f.PatientId == state.PatientId.Value && !f.IsLocked)
            .OrderByDescending(f => f.LastModifiedUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (intake is null)
            return;

        // Update intake with final submitted state
        var submittedState = new IntakeResponseDraft();
        CopyDraftProperties(state, submittedState);
        submittedState.IsSubmitted = true;
        intake.ResponseJson = SerializeDraft(submittedState);
        intake.StructuredDataJson = SerializeStructuredData(submittedState.StructuredData);
        intake.PainMapData = BuildPainMapJson(submittedState);
        intake.Consents = BuildConsentsJson(state);
        intake.SubmittedAt = DateTime.UtcNow;
        intake.LastModifiedUtc = DateTime.UtcNow;
        intake.ModifiedByUserId = userId;
        intake.SyncState = SyncState.Pending;
        // Note: IsLocked is NOT set here — locking happens when an Evaluation note is signed.

        // Flush all demographic data back to the Patient record.
        var patient = await _context.Patients
            .FirstOrDefaultAsync(p => p.Id == state.PatientId.Value, cancellationToken);

        if (patient is not null)
        {
            var name = state.FullName?.Trim() ?? string.Empty;
            var spaceIndex = name.IndexOf(' ');
            if (!string.IsNullOrWhiteSpace(name))
            {
                patient.FirstName = spaceIndex > 0 ? name[..spaceIndex].Trim() : name;
                patient.LastName = spaceIndex > 0 ? name[(spaceIndex + 1)..].Trim() : string.Empty;
            }

            if (state.DateOfBirth.HasValue)
                patient.DateOfBirth = state.DateOfBirth.Value;

            patient.Email = state.EmailAddress?.Trim() ?? patient.Email;
            patient.Phone = state.PhoneNumber?.Trim() ?? patient.Phone;
            patient.AddressLine1 = state.AddressLine1?.Trim() ?? patient.AddressLine1;
            patient.AddressLine2 = state.AddressLine2?.Trim() ?? patient.AddressLine2;
            patient.City = state.City?.Trim() ?? patient.City;
            patient.State = state.StateOrProvince?.Trim() ?? patient.State;
            patient.ZipCode = state.PostalCode?.Trim() ?? patient.ZipCode;
            patient.EmergencyContactName = state.EmergencyContactName?.Trim() ?? patient.EmergencyContactName;
            patient.EmergencyContactPhone = state.EmergencyContactPhone?.Trim() ?? patient.EmergencyContactPhone;
            patient.PayerInfoJson = BuildPayerInfoJson(state);
            patient.ConsentSigned = state.HipaaAcknowledged;
            if (state.HipaaAcknowledged && patient.ConsentSignedDate is null)
                patient.ConsentSignedDate = DateTime.UtcNow;
            patient.LastModifiedUtc = DateTime.UtcNow;
            patient.ModifiedByUserId = userId;
            patient.SyncState = SyncState.Pending;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static void CopyDraftProperties(IntakeResponseDraft source, IntakeResponseDraft target)
    {
        target.PatientId = source.PatientId;
        target.CurrentStep = source.CurrentStep;
        target.HipaaAcknowledged = source.HipaaAcknowledged;
        target.ConsentToTreatAcknowledged = source.ConsentToTreatAcknowledged;
        target.FullName = source.FullName;
        target.DateOfBirth = source.DateOfBirth;
        target.SexAtBirth = source.SexAtBirth;
        target.EmailAddress = source.EmailAddress;
        target.PhoneNumber = source.PhoneNumber;
        target.AddressLine1 = source.AddressLine1;
        target.AddressLine2 = source.AddressLine2;
        target.City = source.City;
        target.StateOrProvince = source.StateOrProvince;
        target.PostalCode = source.PostalCode;
        target.EmergencyContactName = source.EmergencyContactName;
        target.EmergencyContactPhone = source.EmergencyContactPhone;
        target.InsuranceCompanyName = source.InsuranceCompanyName;
        target.MemberOrPolicyNumber = source.MemberOrPolicyNumber;
        target.GroupNumber = source.GroupNumber;
        target.PayerType = source.PayerType;
        target.InsuranceCoverageType = source.InsuranceCoverageType;
        target.HasCurrentMedications = source.HasCurrentMedications;
        target.HasOtherMedicalConditions = source.HasOtherMedicalConditions;
        target.UsesAssistiveDevices = source.UsesAssistiveDevices;
        target.HasPreviousSurgeriesOrInjuries = source.HasPreviousSurgeriesOrInjuries;
        target.MedicalHistoryNotes = source.MedicalHistoryNotes;
        target.SelectedBodyRegion = source.SelectedBodyRegion;
        target.PainSeverityScore = source.PainSeverityScore;
        target.PainDetailDrafts = source.PainDetailDrafts
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        target.StructuredData = CloneStructuredData(source.StructuredData);
        target.SelectedComorbidities = source.SelectedComorbidities.ToHashSet(StringComparer.OrdinalIgnoreCase);
        target.SelectedAssistiveDevices = source.SelectedAssistiveDevices.ToHashSet(StringComparer.OrdinalIgnoreCase);
        target.SelectedLivingSituations = source.SelectedLivingSituations.ToHashSet(StringComparer.OrdinalIgnoreCase);
        target.SelectedHouseLayoutOptions = source.SelectedHouseLayoutOptions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        target.RecommendedOutcomeMeasures = source.RecommendedOutcomeMeasures.ToHashSet(StringComparer.OrdinalIgnoreCase);
        target.IsSubmitted = source.IsSubmitted;
        target.IsLocked = source.IsLocked;
    }

    private static IntakeResponseDraft DeserializeDraft(IntakeForm intake)
    {
        var draft = DeserializeDraft(intake.ResponseJson, intake.StructuredDataJson, intake.PatientId);
        draft.IntakeId = intake.Id;
        draft.IsLocked = intake.IsLocked;
        draft.IsSubmitted = intake.SubmittedAt.HasValue;
        return draft;
    }

    private static IntakeResponseDraft DeserializeDraft(string json, string? structuredDataJson, Guid patientId)
    {
        try
        {
            var draft = JsonSerializer.Deserialize<IntakeResponseDraft>(json, SerializerOptions);
            if (draft is not null)
            {
                draft.PatientId = patientId;
                if (IntakeStructuredDataJson.TryParse(structuredDataJson, out var structuredData, out _))
                {
                    draft.StructuredData = structuredData;
                }

                IntakeDraftPersistence.NormalizeCanonicalSupplementalSelections(draft);

                return draft;
            }
        }
        catch { /* fall through */ }

        var fallback = new IntakeResponseDraft { PatientId = patientId };
        if (IntakeStructuredDataJson.TryParse(structuredDataJson, out var fallbackStructuredData, out _))
        {
            fallback.StructuredData = fallbackStructuredData;
        }

        IntakeDraftPersistence.NormalizeCanonicalSupplementalSelections(fallback);

        return fallback;
    }

    private static string SerializeDraft(IntakeResponseDraft state)
        => JsonSerializer.Serialize(IntakeDraftPersistence.CreatePersistenceCopy(state), SerializerOptions);

    private static string? SerializeStructuredData(IntakeStructuredDataDto? structuredData)
        => structuredData is null ? null : IntakeStructuredDataJson.Serialize(structuredData);

    private static IntakeResponseDraft Clone(IntakeResponseDraft state)
    {
        var draft = new IntakeResponseDraft();
        CopyDraftProperties(state, draft);
        draft.IntakeId = state.IntakeId;
        draft.PatientId = state.PatientId;
        IntakeDraftPersistence.NormalizeCanonicalSupplementalSelections(draft);
        return draft;
    }

    private string BuildPainMapJson(IntakeResponseDraft state)
    {
        if (state.StructuredData is not null)
        {
            return IntakeStructuredDataJson.BuildPainMapProjectionJson(state.StructuredData, _intakeReferenceData);
        }

        var payload = new
        {
            selectedBodyRegion = state.SelectedBodyRegion,
            selectedRegions = state.PainDetailDrafts.Keys.ToArray()
        };

        return JsonSerializer.Serialize(payload, SerializerOptions);
    }

    private static IntakeStructuredDataDto? CloneStructuredData(IntakeStructuredDataDto? structuredData)
    {
        if (structuredData is null)
        {
            return null;
        }

        var json = IntakeStructuredDataJson.Serialize(structuredData);
        return IntakeStructuredDataJson.TryParse(json, out var clone, out _)
            ? clone
            : new IntakeStructuredDataDto();
    }

    private static string BuildPayerInfoJson(IntakeResponseDraft state)
    {
        var payerInfo = new
        {
            PayerType = state.PayerType,
            InsuranceCompanyName = state.InsuranceCompanyName,
            MemberOrPolicyNumber = state.MemberOrPolicyNumber,
            GroupNumber = state.GroupNumber,
            CoverageType = state.InsuranceCoverageType
        };
        return JsonSerializer.Serialize(payerInfo);
    }

    private static string BuildConsentsJson(IntakeResponseDraft state)
    {
        var consents = new
        {
            HipaaAcknowledged = state.HipaaAcknowledged,
            ConsentToTreat = state.ConsentToTreatAcknowledged
        };
        return JsonSerializer.Serialize(consents);
    }

    private static string GenerateAccessTokenPlaceholderHash()
    {
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

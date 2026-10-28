using Microsoft.EntityFrameworkCore;
using Moq;
using System.Text.Json;
using PTDoc.Application.DTOs;
using PTDoc.Application.Identity;
using PTDoc.Application.Intake;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Outcomes;
using PTDoc.Infrastructure.ReferenceData;
using PTDoc.Infrastructure.Services;

namespace PTDoc.Tests.Intake;

[Trait("Category", "CoreCi")]
public sealed class IntakeServiceTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly IntakeService _service;
    private readonly Mock<ITenantContextAccessor> _tenantContext = new();
    private readonly Mock<IIdentityContextAccessor> _identityContext = new();
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    public IntakeServiceTests()
    {
        _tenantContext.Setup(x => x.GetCurrentClinicId()).Returns(_clinicId);
        _identityContext.Setup(x => x.GetCurrentUserId()).Returns(_userId);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"IntakeService_{Guid.NewGuid()}")
            .Options;

        _context = new ApplicationDbContext(options);
        var intakeReferenceData = new IntakeReferenceDataCatalogService();
        var outcomeRegistry = new OutcomeMeasureRegistry();
        var intakeBodyPartMapper = new IntakeBodyPartMapper(intakeReferenceData);
        var draftCanonicalizer = new IntakeDraftCanonicalizer(outcomeRegistry, intakeBodyPartMapper, intakeReferenceData);
        _service = new IntakeService(
            _context,
            _tenantContext.Object,
            _identityContext.Object,
            intakeReferenceData,
            draftCanonicalizer);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProfilePrefill_LegacySourceRejectsNewNormalizedPolicy(bool secondary)
    {
        var patient = CreatePatient("Legacy", "Source");
        patient.PayerInfoJson = """{"insuranceCompanyName":"Old primary","memberOrPolicyNumber":"OLD-P","secondaryInsuranceCompanyName":"Old secondary","secondaryMemberOrPolicyNumber":"OLD-S"}""";
        _context.Patients.Add(patient);
        await _context.SaveChangesAsync();
        var draft = (await _service.EnsureDraftAsync(patient.Id)).Draft!;
        var policy = new PatientInsurancePolicy
        {
            PatientId = patient.Id, ClinicId = _clinicId,
            CoveragePriority = secondary ? InsuranceCoveragePriority.Secondary : InsuranceCoveragePriority.Primary,
            CarrierDisplayName = "Clinician policy", MemberOrPolicyNumber = "CURRENT"
        };
        _context.PatientInsurancePolicies.Add(policy);
        await _context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SubmitAsync(draft));
        Assert.Equal("CURRENT", policy.MemberOrPolicyNumber);
        Assert.False((await _context.IntakeForms.SingleAsync()).IsLocked);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProfilePrefill_MissingExactVersionRejectsDelayedCorrectionBelowAggregate(bool secondary)
    {
        var patient = CreatePatient("Legacy", "Timestamp");
        var policy = new PatientInsurancePolicy
        {
            PatientId = patient.Id, ClinicId = _clinicId,
            CoveragePriority = secondary ? InsuranceCoveragePriority.Secondary : InsuranceCoveragePriority.Primary,
            CarrierDisplayName = "Original", LastModifiedUtc = patient.LastModifiedUtc.AddHours(-1)
        };
        _context.AddRange(patient, policy);
        await _context.SaveChangesAsync();
        var draft = (await _service.EnsureDraftAsync(patient.Id)).Draft!;
        if (secondary) draft.PrefilledSecondaryPolicyModifiedUtc = null;
        else draft.PrefilledPrimaryPolicyModifiedUtc = null;
        var intake = await _context.IntakeForms.SingleAsync();
        intake.ResponseJson = IntakeDraftPersistence.SerializePersistenceJson(draft);
        policy.MemberOrPolicyNumber = "DELAYED-CORRECTION";
        policy.LastModifiedUtc = policy.LastModifiedUtc.AddMinutes(30);
        await _context.SaveChangesAsync();
        Assert.True(policy.LastModifiedUtc < draft.ProfilePrefillSourceModifiedUtc);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SubmitAsync(draft));
        Assert.Equal("DELAYED-CORRECTION", policy.MemberOrPolicyNumber);
        Assert.False(intake.IsLocked);
    }

    [Fact]
    public async Task ProfilePrefill_ServiceSaveAndSubmitPreserveSourceAndNewerDemographics()
    {
        var patient = CreatePatient("Mary Jane", "Smith");
        patient.Phone = "OLD";
        patient.AddressLine1 = "OLD ADDRESS";
        _context.Patients.Add(patient);
        await _context.SaveChangesAsync();
        var draft = (await _service.EnsureDraftAsync(patient.Id)).Draft!;
        patient.FirstName = "Corrected Compound";
        patient.Phone = "CURRENT";
        patient.AddressLine1 = "CURRENT ADDRESS";
        // Offline corrections need not have a timestamp newer than the prefill.
        patient.LastModifiedUtc = patient.LastModifiedUtc.AddMinutes(-1);
        await _context.SaveChangesAsync();
        draft.PrefilledDemographics = null;
        draft.ProfilePrefillSourceModifiedUtc = null;
        draft.MedicalHistoryNotes = "Keep this answer";
        await _service.SaveDraftAsync(draft);
        Assert.NotNull((await _service.GetLatestByPatientIdAsync(patient.Id))!.PrefilledDemographics);
        await _service.SubmitAsync(draft);
        Assert.Equal("Corrected Compound", patient.FirstName);
        Assert.Equal("CURRENT", patient.Phone);
        Assert.Equal("CURRENT ADDRESS", patient.AddressLine1);
        var submitted = (await _service.GetLatestByPatientIdAsync(patient.Id))!;
        Assert.Equal("Mary Jane Smith", submitted.FullName);
        Assert.Equal("OLD", submitted.PhoneNumber);
        Assert.Equal("Keep this answer", submitted.MedicalHistoryNotes);
    }

    [Fact]
    public async Task ProfilePrefill_ReconciliationRejectsStaleOfferAndPreservesOtherAnswers()
    {
        var patient = CreatePatient("Saved", "Patient");
        var policy = new PatientInsurancePolicy { PatientId = patient.Id, ClinicId = _clinicId, CarrierDisplayName = "Original" };
        _context.AddRange(patient, policy);
        await _context.SaveChangesAsync();
        var draft = (await _service.EnsureDraftAsync(patient.Id)).Draft!;
        draft.MedicalHistoryNotes = "Keep this answer";
        draft.ProfileUpdate = new IntakeProfileUpdate { Action = IntakeProfileAction.UsePrimary, ExpectedFingerprint = draft.ProfilePrefill!.Fingerprint };
        policy.MemberOrPolicyNumber = "CURRENT";
        policy.LastModifiedUtc = policy.LastModifiedUtc.AddMinutes(1);
        await _context.SaveChangesAsync();
        var before = (await _context.IntakeForms.SingleAsync()).ResponseJson;
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SaveDraftAsync(draft));
        Assert.Equal(before, (await _context.IntakeForms.SingleAsync()).ResponseJson);
        draft.ProfileUpdate.ExpectedFingerprint = (await IntakeProfilePrefillReader.ReadAsync(_context, patient.Id, _clinicId))!.Fingerprint;
        await _service.SaveDraftAsync(draft);
        var saved = (await _service.GetLatestByPatientIdAsync(patient.Id))!;
        Assert.Equal("CURRENT", saved.MemberOrPolicyNumber);
        Assert.Equal("Keep this answer", saved.MedicalHistoryNotes);
        Assert.Null(saved.ProfileUpdate);
        await _service.SubmitAsync(saved);
        Assert.True((await _context.IntakeForms.SingleAsync()).IsLocked);
    }

    [Fact]
    public void ProfilePrefill_NewJsonPreservesUnknownFieldsAndExplicitBlanks()
    {
        var profile = new IntakeProfilePrefill { PhoneNumber = "555-0107", AddressLine1 = "Saved address" };
        var json = profile.ApplyToNewResponseJson("""{"PhoneNumber":"","customExtension":{"value":"preserved"}}""");
        using var parsed = JsonDocument.Parse(json);
        Assert.Equal("", parsed.RootElement.GetProperty("PhoneNumber").GetString());
        Assert.Equal("Saved address", parsed.RootElement.GetProperty("addressLine1").GetString());
        Assert.Equal("preserved", parsed.RootElement.GetProperty("customExtension").GetProperty("value").GetString());
    }

    [Fact]
    public async Task ProfilePrefill_NewDraftCopiesDemographicsAndOrderedPolicies_AndReloadPreservesClearedAnswers()
    {
        var patient = CreatePatient("Synthetic", "Prefill");
        patient.Phone = "555-0107";
        patient.AddressLine1 = "100 Fixture Lane";
        patient.EmergencyContactName = "Fixture Contact";
        patient.EmergencyContactPhone = "555-0108";
        var primary = new PatientInsurancePolicy { PatientId = patient.Id, ClinicId = _clinicId, CarrierDisplayName = "Primary Fixture", MemberOrPolicyNumber = "PRIMARY-7", CoveragePriority = InsuranceCoveragePriority.Primary };
        var secondary = new PatientInsurancePolicy { PatientId = patient.Id, ClinicId = _clinicId, CarrierDisplayName = "Secondary Fixture", MemberOrPolicyNumber = "SECONDARY-7", CoveragePriority = InsuranceCoveragePriority.Secondary };
        _context.Patients.Add(patient);
        _context.PatientInsurancePolicies.AddRange(secondary, primary);
        await _context.SaveChangesAsync();

        var created = (await _service.EnsureDraftAsync(patient.Id)).Draft!;
        Assert.Equal(patient.Phone, created.PhoneNumber);
        Assert.Equal(patient.AddressLine1, created.AddressLine1);
        Assert.Equal(primary.MemberOrPolicyNumber, created.MemberOrPolicyNumber);
        Assert.Equal(secondary.MemberOrPolicyNumber, created.SecondaryMemberOrPolicyNumber);
        Assert.Equal(primary.Id, created.PrefilledPrimaryPolicyId);
        Assert.Equal(secondary.Id, created.PrefilledSecondaryPolicyId);
        created.PhoneNumber = ""; // Intentionally cleared, not an uninitialized field.
        created.AddressLine1 = "Patient correction";
        await _service.SaveDraftAsync(created);
        var storedBefore = (await _context.IntakeForms.SingleAsync()).ResponseJson;
        var reopened = (await _service.GetLatestByPatientIdAsync(patient.Id))!;
        Assert.Equal("", reopened.PhoneNumber);
        Assert.Equal("Patient correction", reopened.AddressLine1);
        Assert.Equal(patient.Phone, reopened.ProfilePrefill!.PhoneNumber);
        Assert.Equal(storedBefore, (await _context.IntakeForms.SingleAsync()).ResponseJson);
        reopened.ProfilePrefill.ApplyTo(reopened, confirmedBlankFields: true);
        Assert.Equal(patient.Phone, reopened.PhoneNumber);
        Assert.Equal("Patient correction", reopened.AddressLine1);
    }

    [Fact]
    public async Task ProfilePrefill_RejectsOtherClinic_AndDoesNotOfferLockedAnswers()
    {
        var patient = CreatePatient("Synthetic", "OtherClinic");
        patient.ClinicId = Guid.NewGuid();
        _context.Patients.Add(patient);
        await _context.SaveChangesAsync();
        Assert.Equal(IntakeEnsureDraftStatus.PatientNotFound, (await _service.EnsureDraftAsync(patient.Id)).Status);
        Assert.Null(await IntakeProfilePrefillReader.ReadAsync(_context, patient.Id, _clinicId));

        patient.ClinicId = _clinicId;
        await _context.SaveChangesAsync();
        var draft = (await _service.EnsureDraftAsync(patient.Id)).Draft!;
        var intake = await _context.IntakeForms.SingleAsync();
        intake.IsLocked = true;
        intake.SubmittedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        Assert.Null((await _service.GetLatestByPatientIdAsync(patient.Id))!.ProfilePrefill);
        var locked = new IntakeResponseDraft { IsLocked = true };
        new IntakeProfilePrefill { PhoneNumber = "555-0101" }.ApplyTo(locked, true);
        Assert.Null(locked.PhoneNumber);
    }

    [Fact]
    public async Task ProfilePrefill_SkipsAmbiguousAndArchivedPolicies_AndDoesNotMixPolicyAnswers()
    {
        var patient = CreatePatient("Synthetic", "Ambiguous");
        patient.PayerInfoJson = """{"insuranceCompanyName":"Stale legacy"}""";
        _context.Patients.Add(patient);
        _context.PatientInsurancePolicies.AddRange(
            new PatientInsurancePolicy { PatientId = patient.Id, ClinicId = _clinicId, CoveragePriority = InsuranceCoveragePriority.Primary, CarrierDisplayName = "One" },
            new PatientInsurancePolicy { PatientId = patient.Id, ClinicId = _clinicId, CoveragePriority = InsuranceCoveragePriority.Primary, CarrierDisplayName = "Two" },
            new PatientInsurancePolicy { PatientId = patient.Id, ClinicId = _clinicId, CoveragePriority = InsuranceCoveragePriority.Secondary, CarrierDisplayName = "Archived", IsArchived = true });
        await _context.SaveChangesAsync();
        var profile = (await IntakeProfilePrefillReader.ReadAsync(_context, patient.Id, _clinicId))!;
        Assert.Null(profile.InsuranceCompanyName);
        Assert.Null(profile.SecondaryInsuranceCompanyName);
        var edited = new IntakeResponseDraft { InsuranceCompanyName = "Patient's other policy", PhoneNumber = "" };
        new IntakeProfilePrefill { InsuranceCompanyName = "Saved carrier", MemberOrPolicyNumber = "DO-NOT-MIX", PhoneNumber = "555-0101" }.ApplyTo(edited);
        Assert.Null(edited.MemberOrPolicyNumber);
        Assert.Equal("", edited.PhoneNumber);
    }

    [Fact]
    public async Task ProfilePrefill_LegacyAliasesRemainReadable_WithoutImportingClinicalHistory()
    {
        var patient = CreatePatient("Synthetic", "Legacy");
        patient.PayerInfoJson = """{"InsuranceCompanyName":"Legacy primary","MemberIdPolicyNumber":"P7","SecondaryInsuranceCompanyName":"Legacy secondary","SecondaryMemberIdPolicyNumber":"S7","MedicalHistoryNotes":"Do not copy"}""";
        _context.Patients.Add(patient);
        await _context.SaveChangesAsync();
        var created = (await _service.EnsureDraftAsync(patient.Id)).Draft!;
        Assert.Equal("P7", created.MemberOrPolicyNumber);
        Assert.Equal("S7", created.SecondaryMemberOrPolicyNumber);
        Assert.Null(created.MedicalHistoryNotes);
    }

    [Theory]
    [InlineData("commercial", "Commercial")]
    [InlineData("medicare", "Medicare")]
    [InlineData("medicaid", "Medicaid")]
    [InlineData(" WORKERS_COMP ", "Workers' Compensation")]
    [InlineData("motor_vehicle", "Motor Vehicle")]
    [InlineData("liability", "Liability")]
    [InlineData("self_pay", "Self Pay")]
    public async Task ProfilePrefill_LegacyProviderTypeUsesIntakeLabels(string providerType, string expected)
    {
        var patient = CreatePatient("Synthetic", "Legacy");
        patient.PayerInfoJson = JsonSerializer.Serialize(new { ProviderType = providerType, AdjusterName = "Saved adjuster" });
        _context.Patients.Add(patient);
        await _context.SaveChangesAsync();

        var draft = (await _service.EnsureDraftAsync(patient.Id)).Draft!;
        Assert.Equal(expected, draft.PayerType);
        Assert.Equal("Saved adjuster", draft.AdjusterName);

        patient.PayerInfoJson = """{"payerType":"Medicare","providerType":"workers_comp"}""";
        await _context.SaveChangesAsync();
        var profile = await IntakeProfilePrefillReader.ReadAsync(_context, patient.Id, _clinicId);
        Assert.Equal("Medicare", profile!.PayerType);
    }

    [Theory]
    [InlineData(nameof(IntakeProfilePrefill.PayerType), "Self Pay")]
    [InlineData(nameof(IntakeProfilePrefill.GroupNumber), "GROUP-7")]
    [InlineData(nameof(IntakeProfilePrefill.AdjusterName), "Saved adjuster")]
    [InlineData(nameof(IntakeProfilePrefill.AdjusterPhone), "555-0107")]
    [InlineData(nameof(IntakeProfilePrefill.AdjusterEmail), "adjuster@example.test")]
    [InlineData(nameof(IntakeProfilePrefill.AdjusterFax), "555-0108")]
    [InlineData(nameof(IntakeProfilePrefill.SecondaryGroupNumber), "SECONDARY-GROUP-7")]
    public void ProfilePrefill_OffersEverySupportedPolicyField_WithoutMixingExistingPolicies(string field, string value)
    {
        var profile = new IntakeProfilePrefill();
        typeof(IntakeProfilePrefill).GetProperty(field)!.SetValue(profile, value);
        var draft = new IntakeResponseDraft { FullName = "Already populated" };

        Assert.True(profile.HasValuesForBlankFields(draft));
        profile.ApplyTo(draft, confirmedBlankFields: true);
        Assert.Equal(value, typeof(IntakeResponseDraft).GetProperty(field)!.GetValue(draft));
        Assert.False(profile.HasValuesForBlankFields(draft));

        var answered = new IntakeResponseDraft
        {
            InsuranceCompanyName = "Patient's primary",
            SecondaryInsuranceCompanyName = "Patient's secondary"
        };
        Assert.False(profile.HasValuesForBlankFields(answered));
        profile.ApplyTo(answered, confirmedBlankFields: true);
        Assert.Null(typeof(IntakeResponseDraft).GetProperty(field)!.GetValue(answered));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProfilePrefill_SubmitPreservesCompoundNameAndPolicyIdentity(bool editName)
    {
        var patient = CreatePatient("Mary Jane", "Smith");
        var policy = new PatientInsurancePolicy
        {
            PatientId = patient.Id,
            ClinicId = _clinicId,
            CoveragePriority = InsuranceCoveragePriority.Primary,
            CarrierDisplayName = "Saved carrier",
            MemberOrPolicyNumber = "MEMBER-7",
            LastModifiedUtc = DateTime.UtcNow
        };
        _context.Patients.Add(patient);
        _context.PatientInsurancePolicies.Add(policy);
        await _context.SaveChangesAsync();
        var draft = (await _service.EnsureDraftAsync(patient.Id)).Draft!;
        Assert.Equal(policy.LastModifiedUtc, draft.PrefilledPrimaryPolicyModifiedUtc);
        if (editName) draft.FullName = "Janet Jones";

        await _service.SubmitAsync(draft);
        _context.ChangeTracker.Clear();
        var savedPatient = await _context.Patients.SingleAsync();
        Assert.Equal(editName ? "Janet" : "Mary Jane", savedPatient.FirstName);
        Assert.Equal(editName ? "Jones" : "Smith", savedPatient.LastName);
        Assert.Equal(policy.Id, (await _context.PatientInsurancePolicies.SingleAsync()).Id);
        var submitted = (await _service.GetLatestByPatientIdAsync(patient.Id))!;
        Assert.True(submitted.IsLocked);
        Assert.Equal(draft.PrefilledPrimaryPolicyModifiedUtc, submitted.PrefilledPrimaryPolicyModifiedUtc);
    }

    [Theory]
    [InlineData(false, "replaced")]
    [InlineData(true, "replaced")]
    [InlineData(false, "reordered")]
    [InlineData(true, "reordered")]
    [InlineData(false, "changed")]
    [InlineData(true, "changed")]
    [InlineData(false, "changed-below-profile-timestamp")]
    [InlineData(true, "changed-below-profile-timestamp")]
    [InlineData(false, "archived")]
    [InlineData(true, "archived")]
    [InlineData(false, "inactive")]
    [InlineData(true, "inactive")]
    [InlineData(false, "ambiguous")]
    [InlineData(true, "ambiguous")]
    [InlineData(false, "other-patient")]
    [InlineData(true, "other-clinic")]
    [InlineData(false, "legacy-changed")]
    [InlineData(true, "missing-timestamp")]
    public async Task ProfilePrefill_RejectsStalePolicyBeforeMutatingSubmission(bool secondary, string change)
    {
        var patient = CreatePatient("Mary Jane", "Smith");
        var priority = secondary ? InsuranceCoveragePriority.Secondary : InsuranceCoveragePriority.Primary;
        var policy = new PatientInsurancePolicy
        {
            PatientId = patient.Id,
            ClinicId = _clinicId,
            CoveragePriority = priority,
            CarrierDisplayName = "Original",
            MemberOrPolicyNumber = "ORIGINAL",
            LastModifiedUtc = patient.LastModifiedUtc
        };
        var authorization = new PatientInsuranceAuthorization
        {
            PatientId = patient.Id,
            ClinicId = _clinicId,
            PatientInsurancePolicyId = policy.Id,
            ReferenceNumber = "KEEP-AUTH"
        };
        if (change == "changed-below-profile-timestamp") policy.LastModifiedUtc = patient.LastModifiedUtc.AddHours(-1);
        _context.Patients.Add(patient);
        _context.PatientInsurancePolicies.Add(policy);
        _context.PatientInsuranceAuthorizations.Add(authorization);
        await _context.SaveChangesAsync();
        var draft = (await _service.EnsureDraftAsync(patient.Id)).Draft!;
        var intake = await _context.IntakeForms.SingleAsync();
        var originalJson = intake.ResponseJson;
        var originalPatientVersion = patient.LastModifiedUtc;
        draft.FullName = "Must Not Change";
        draft.ReferringDoctorName = "Must Not Create Provider";
        switch (change)
        {
            case "replaced":
            case "ambiguous":
                policy.IsArchived = change == "replaced";
                _context.PatientInsurancePolicies.Add(new PatientInsurancePolicy
                {
                    PatientId = patient.Id,
                    ClinicId = _clinicId,
                    CoveragePriority = priority,
                    CarrierDisplayName = "Replacement",
                    MemberOrPolicyNumber = "REPLACEMENT"
                });
                break;
            case "reordered":
                policy.CoveragePriority = secondary ? InsuranceCoveragePriority.Primary : InsuranceCoveragePriority.Secondary;
                break;
            case "changed":
            case "legacy-changed":
                policy.MemberOrPolicyNumber = "CLINICIAN-CORRECTION";
                policy.LastModifiedUtc = patient.LastModifiedUtc.AddMinutes(1);
                if (change == "legacy-changed") draft.PrefilledPrimaryPolicyModifiedUtc = null;
                break;
            case "archived": policy.IsArchived = true; break;
            case "inactive": policy.Status = InsurancePolicyStatus.Inactive; break;
            case "changed-below-profile-timestamp": policy.LastModifiedUtc = policy.LastModifiedUtc.AddMinutes(1); break;
            case "other-patient": policy.PatientId = Guid.NewGuid(); break;
            case "other-clinic": policy.ClinicId = Guid.NewGuid(); break;
            case "missing-timestamp":
                draft.PrefilledSecondaryPolicyModifiedUtc = null;
                draft.ProfilePrefillSourceModifiedUtc = null;
                break;
        }
        if (change is "legacy-changed" or "missing-timestamp")
        {
            // Model a previously stored legacy draft; client metadata can no longer replace it.
            var legacy = JsonSerializer.Deserialize<IntakeResponseDraft>(originalJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            legacy.PrefilledPrimaryPolicyModifiedUtc = draft.PrefilledPrimaryPolicyModifiedUtc;
            legacy.PrefilledSecondaryPolicyModifiedUtc = draft.PrefilledSecondaryPolicyModifiedUtc;
            legacy.ProfilePrefillSourceModifiedUtc = draft.ProfilePrefillSourceModifiedUtc;
            intake.ResponseJson = originalJson = IntakeDraftPersistence.SerializePersistenceJson(legacy);
        }
        await _context.SaveChangesAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SubmitAsync(draft));
        Assert.Equal(IntakeDomainProjection.StalePolicySourceMessage, error.Message);
        Assert.False(intake.IsLocked);
        Assert.Null(intake.SubmittedAt);
        Assert.Equal(originalJson, intake.ResponseJson);
        Assert.Equal("Mary Jane", patient.FirstName);
        Assert.Equal(originalPatientVersion, patient.LastModifiedUtc);
        Assert.False(_context.ChangeTracker.HasChanges());
        Assert.Equal("KEEP-AUTH", authorization.ReferenceNumber);
        Assert.Equal(policy.Id, authorization.PatientInsurancePolicyId);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            IntakeDomainProjection.UpsertAsync(_context, patient, draft, _userId, default));
        Assert.False(_context.ChangeTracker.HasChanges());
    }

    [Fact]
    public void ProfilePrefill_FillingDemographicsDoesNotRefreshAcceptedPolicyVersions()
    {
        var original = DateTime.UtcNow.AddHours(-1);
        var draft = new IntakeResponseDraft
        {
            InsuranceCompanyName = "Old primary",
            SecondaryInsuranceCompanyName = "Old secondary",
            PrefilledPrimaryPolicyModifiedUtc = original,
            PrefilledSecondaryPolicyModifiedUtc = original,
            ProfilePrefillSourceModifiedUtc = original
        };
        var profile = new IntakeProfilePrefill
        {
            PhoneNumber = "555-0107",
            SourceModifiedUtc = DateTime.UtcNow,
            PrimaryPolicyModifiedUtc = DateTime.UtcNow,
            SecondaryPolicyModifiedUtc = DateTime.UtcNow
        };

        profile.ApplyTo(draft, confirmedBlankFields: true);
        Assert.Equal("555-0107", draft.PhoneNumber);
        Assert.Equal(original, draft.PrefilledPrimaryPolicyModifiedUtc);
        Assert.Equal(original, draft.PrefilledSecondaryPolicyModifiedUtc);
        Assert.Equal(original, draft.ProfilePrefillSourceModifiedUtc);
    }

    [Fact]
    public async Task EnsureDraftAsync_ReturnsExistingUnlockedDraft_WhenPresent()
    {
        var patient = CreatePatient("Avery", "Existing");
        var intake = new IntakeForm
        {
            Id = Guid.NewGuid(),
            PatientId = patient.Id,
            ResponseJson = """{"fullName":"Avery Existing"}""",
            PainMapData = "{}",
            Consents = "{}",
            TemplateVersion = "1.0",
            IsLocked = false,
            AccessToken = "token",
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = _userId,
            ClinicId = _clinicId
        };

        _context.Patients.Add(patient);
        _context.IntakeForms.Add(intake);
        await _context.SaveChangesAsync();

        var result = await _service.EnsureDraftAsync(patient.Id);

        Assert.Equal(IntakeEnsureDraftStatus.Existing, result.Status);
        Assert.NotNull(result.Draft);
        Assert.Equal(intake.Id, result.Draft!.IntakeId);
        Assert.Equal(patient.Id, result.Draft.PatientId);
    }

    [Fact]
    public async Task EnsureDraftAsync_CreatesDraft_WhenPatientIsEligible()
    {
        var patient = CreatePatient("Casey", "Create");
        _context.Patients.Add(patient);
        await _context.SaveChangesAsync();

        var result = await _service.EnsureDraftAsync(patient.Id, new IntakeResponseDraft
        {
            PatientId = patient.Id,
            FullName = "Casey Create",
            HipaaAcknowledged = true
        });

        Assert.Equal(IntakeEnsureDraftStatus.Created, result.Status);
        Assert.NotNull(result.Draft);
        Assert.True(result.Draft!.IntakeId.HasValue);

        var storedDraft = await _context.IntakeForms.SingleAsync(form => form.PatientId == patient.Id);
        Assert.Equal(result.Draft.IntakeId, storedDraft.Id);
        Assert.False(storedDraft.IsLocked);
    }

    [Fact]
    public async Task EnsureDraftAsync_CreatesDraft_WhenSeedIsEmpty()
    {
        var patient = CreatePatient("Emery", "EmptySeed");
        _context.Patients.Add(patient);
        await _context.SaveChangesAsync();

        var result = await _service.EnsureDraftAsync(patient.Id, new IntakeResponseDraft
        {
            PatientId = patient.Id
        });

        Assert.Equal(IntakeEnsureDraftStatus.Created, result.Status);
        Assert.NotNull(result.Draft);

        var storedDraft = await _context.IntakeForms.SingleAsync(form => form.PatientId == patient.Id);
        Assert.False(storedDraft.IsLocked);
        Assert.False(string.IsNullOrWhiteSpace(storedDraft.Consents));
    }

    [Fact]
    public async Task EnsureDraftAsync_PersistsStructuredDataJson_WhenStructuredDataExists()
    {
        var patient = CreatePatient("Jordan", "Structured");
        _context.Patients.Add(patient);
        await _context.SaveChangesAsync();

        var structuredData = new IntakeStructuredDataDto
        {
            BodyPartSelections =
            [
                new IntakeBodyPartSelectionDto
                {
                    BodyPartId = "knee",
                    Lateralities = ["left"]
                }
            ],
            ComorbidityIds = ["hypertension"]
        };

        await _service.EnsureDraftAsync(patient.Id, new IntakeResponseDraft
        {
            PatientId = patient.Id,
            FullName = "Jordan Structured",
            StructuredData = structuredData
        });

        var storedDraft = await _context.IntakeForms.SingleAsync(form => form.PatientId == patient.Id);

        Assert.False(string.IsNullOrWhiteSpace(storedDraft.StructuredDataJson));
        Assert.Contains("bodyPartSelections", storedDraft.StructuredDataJson, StringComparison.Ordinal);
        Assert.Contains("selectedBodyPartIds", storedDraft.PainMapData, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnsureDraftAsync_ClearsLegacySupplementalSelectionsFromPersistedResponseJson_WhenStructuredIdsExist()
    {
        var patient = CreatePatient("Morgan", "Canonical");
        _context.Patients.Add(patient);
        await _context.SaveChangesAsync();

        await _service.EnsureDraftAsync(patient.Id, new IntakeResponseDraft
        {
            PatientId = patient.Id,
            FullName = "Morgan Canonical",
            StructuredData = new IntakeStructuredDataDto
            {
                ComorbidityIds = ["hypertension"],
                AssistiveDeviceIds = ["cane"],
                LivingSituationIds = ["lives-alone"],
                HouseLayoutOptionIds = ["single-story-main-floor-bed-bath"]
            },
            SelectedComorbidities = ["Hypertension (High Blood Pressure)"],
            SelectedAssistiveDevices = ["Cane"],
            SelectedLivingSituations = ["Lives alone"],
            SelectedHouseLayoutOptions = ["Single-Story Home: Bedroom and bathroom on main floor"]
        });

        var storedDraft = await _context.IntakeForms.SingleAsync(form => form.PatientId == patient.Id);

        using var responseJson = JsonDocument.Parse(storedDraft.ResponseJson);
        Assert.Equal(0, responseJson.RootElement.GetProperty("selectedComorbidities").GetArrayLength());
        Assert.Equal(0, responseJson.RootElement.GetProperty("selectedAssistiveDevices").GetArrayLength());
        Assert.Equal(0, responseJson.RootElement.GetProperty("selectedLivingSituations").GetArrayLength());
        Assert.Equal(0, responseJson.RootElement.GetProperty("selectedHouseLayoutOptions").GetArrayLength());
    }

    [Fact]
    public async Task EnsureDraftAsync_PersistsCanonicalConsentPacketInResponseJson_WhenLegacyConsentFlagsSeedDraft()
    {
        var patient = CreatePatient("Quinn", "Consent");
        _context.Patients.Add(patient);
        await _context.SaveChangesAsync();

        await _service.EnsureDraftAsync(patient.Id, new IntakeResponseDraft
        {
            PatientId = patient.Id,
            FullName = "Quinn Consent",
            PhoneNumber = "555-0123",
            EmailAddress = "quinn@example.com",
            HipaaAcknowledged = true,
            ConsentToTreatAcknowledged = true,
            AllowPhoneCalls = true,
            AllowEmailMessages = true
        });

        var storedDraft = await _context.IntakeForms.SingleAsync(form => form.PatientId == patient.Id);

        using var responseJson = JsonDocument.Parse(storedDraft.ResponseJson);
        var consentPacket = responseJson.RootElement.GetProperty("consentPacket");
        Assert.True(consentPacket.GetProperty("hipaaAcknowledged").GetBoolean());
        Assert.True(consentPacket.GetProperty("treatmentConsentAccepted").GetBoolean());
        Assert.Equal("555-0123", consentPacket.GetProperty("communicationPhoneNumber").GetString());
        Assert.Equal("quinn@example.com", consentPacket.GetProperty("communicationEmail").GetString());
        Assert.False(responseJson.RootElement.TryGetProperty("hipaaAcknowledged", out _));
        Assert.False(responseJson.RootElement.TryGetProperty("consentToTreatAcknowledged", out _));

        using var consentsJson = JsonDocument.Parse(storedDraft.Consents);
        Assert.True(consentsJson.RootElement.GetProperty("hipaaAcknowledged").GetBoolean());
        Assert.True(consentsJson.RootElement.GetProperty("treatmentConsentAccepted").GetBoolean());
    }

    [Fact]
    public async Task GetDraftByPatientIdAsync_RehydratesStructuredDataFromCanonicalJson()
    {
        var patient = CreatePatient("Taylor", "Rehydrate");
        var structuredDataJson = IntakeStructuredDataJson.Serialize(new IntakeStructuredDataDto
        {
            BodyPartSelections =
            [
                new IntakeBodyPartSelectionDto
                {
                    BodyPartId = "shoulder",
                    Lateralities = ["right"]
                }
            ],
            MedicationIds = ["ibuprofen-advil-motrin"]
        });

        _context.Patients.Add(patient);
        _context.IntakeForms.Add(new IntakeForm
        {
            Id = Guid.NewGuid(),
            PatientId = patient.Id,
            ResponseJson = """{"fullName":"Taylor Rehydrate"}""",
            StructuredDataJson = structuredDataJson,
            PainMapData = "{}",
            Consents = "{}",
            TemplateVersion = "1.0",
            IsLocked = false,
            AccessToken = "token",
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = _userId,
            ClinicId = _clinicId
        });
        await _context.SaveChangesAsync();

        var draft = await _service.GetDraftByPatientIdAsync(patient.Id);

        Assert.NotNull(draft);
        Assert.NotNull(draft!.StructuredData);
        Assert.Equal("shoulder", draft.StructuredData!.BodyPartSelections[0].BodyPartId);
        Assert.Equal(["right"], draft.StructuredData.BodyPartSelections[0].Lateralities);
        Assert.Equal(["ibuprofen-advil-motrin"], draft.StructuredData.MedicationIds);
    }

    [Fact]
    public async Task GetDraftByPatientIdAsync_ClearsLegacySupplementalSelections_WhenStructuredDataExists()
    {
        var patient = CreatePatient("Skyler", "Normalize");
        var structuredDataJson = IntakeStructuredDataJson.Serialize(new IntakeStructuredDataDto
        {
            ComorbidityIds = ["hypertension"],
            AssistiveDeviceIds = ["cane"]
        });

        _context.Patients.Add(patient);
        _context.IntakeForms.Add(new IntakeForm
        {
            Id = Guid.NewGuid(),
            PatientId = patient.Id,
            ResponseJson = """
                           {"selectedComorbidities":["Hypertension (High Blood Pressure)"],"selectedAssistiveDevices":["Cane"]}
                           """,
            StructuredDataJson = structuredDataJson,
            PainMapData = "{}",
            Consents = "{}",
            TemplateVersion = "1.0",
            IsLocked = false,
            AccessToken = "token",
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = _userId,
            ClinicId = _clinicId
        });
        await _context.SaveChangesAsync();

        var draft = await _service.GetDraftByPatientIdAsync(patient.Id);

        Assert.NotNull(draft);
        Assert.Empty(draft!.SelectedComorbidities);
        Assert.Empty(draft.SelectedAssistiveDevices);
        Assert.Equal(["hypertension"], draft.StructuredData!.ComorbidityIds);
        Assert.Equal(["cane"], draft.StructuredData.AssistiveDeviceIds);
    }

    [Fact]
    public async Task GetDraftByPatientIdAsync_RebuildsCanonicalOutcomeRecommendationsFromStructuredBodyParts()
    {
        var patient = CreatePatient("Riley", "Recommendations");
        var structuredData = new IntakeStructuredDataDto
        {
            SchemaVersion = "2026-03-30",
            BodyPartSelections =
            [
                new IntakeBodyPartSelectionDto
                {
                    BodyPartId = "knee",
                    Lateralities = ["left"]
                }
            ]
        };

        _context.Patients.Add(patient);
        _context.IntakeForms.Add(new IntakeForm
        {
            Id = Guid.NewGuid(),
            PatientId = patient.Id,
            ResponseJson = """
                           {"recommendedOutcomeMeasures":["LEFS","KOOS"]}
                           """,
            StructuredDataJson = IntakeStructuredDataJson.Serialize(structuredData),
            PainMapData = "{}",
            Consents = "{}",
            TemplateVersion = "1.0",
            IsLocked = false,
            AccessToken = "token",
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = _userId,
            ClinicId = _clinicId
        });
        await _context.SaveChangesAsync();

        var draft = await _service.GetDraftByPatientIdAsync(patient.Id);

        Assert.NotNull(draft);
        Assert.Equal(["LEFS", "NPRS", "PSFS"], draft!.RecommendedOutcomeMeasures.OrderBy(value => value).ToArray());
    }

    [Fact]
    public async Task GetDraftByPatientIdAsync_HydratesLegacyConsentFlagsFromCanonicalConsentPacket()
    {
        var patient = CreatePatient("Dakota", "ConsentHydrate");

        _context.Patients.Add(patient);
        _context.IntakeForms.Add(new IntakeForm
        {
            Id = Guid.NewGuid(),
            PatientId = patient.Id,
            ResponseJson = """
                           {"consentPacket":{"hipaaAcknowledged":true,"treatmentConsentAccepted":true,"communicationCallConsent":true,"communicationPhoneNumber":"555-0142","communicationEmailConsent":true,"communicationEmail":"dakota@example.com"}}
                           """,
            PainMapData = "{}",
            Consents = "{}",
            TemplateVersion = "1.0",
            IsLocked = false,
            AccessToken = "token",
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = _userId,
            ClinicId = _clinicId
        });
        await _context.SaveChangesAsync();

        var draft = await _service.GetDraftByPatientIdAsync(patient.Id);

        Assert.NotNull(draft);
        Assert.True(draft!.HipaaAcknowledged);
        Assert.True(draft.ConsentToTreatAcknowledged);
        Assert.True(draft.AllowPhoneCalls);
        Assert.True(draft.AllowEmailMessages);
        Assert.NotNull(draft.ConsentPacket);
        Assert.Equal("555-0142", draft.ConsentPacket!.CommunicationPhoneNumber);
        Assert.Equal("dakota@example.com", draft.ConsentPacket.CommunicationEmail);
    }

    [Fact]
    public async Task GetDraftByPatientIdAsync_ReturnsNull_WhenOnlyLockedIntakeExists()
    {
        var patient = CreatePatient("Locke", "DraftOnly");
        _context.Patients.Add(patient);
        _context.IntakeForms.Add(new IntakeForm
        {
            Id = Guid.NewGuid(),
            PatientId = patient.Id,
            ResponseJson = """{"fullName":"Locke DraftOnly"}""",
            PainMapData = "{}",
            Consents = "{}",
            TemplateVersion = "1.0",
            IsLocked = true,
            SubmittedAt = new DateTime(2026, 5, 1, 14, 0, 0, DateTimeKind.Utc),
            AccessToken = "token",
            LastModifiedUtc = new DateTime(2026, 5, 1, 14, 0, 0, DateTimeKind.Utc),
            ModifiedByUserId = _userId,
            ClinicId = _clinicId
        });
        await _context.SaveChangesAsync();

        var draft = await _service.GetDraftByPatientIdAsync(patient.Id);

        Assert.Null(draft);
    }

    [Fact]
    public async Task GetLatestByPatientIdAsync_ReturnsLockedSubmittedLatestIntake()
    {
        var patient = CreatePatient("Latest", "Locked");
        var olderDraftId = Guid.NewGuid();
        var submittedId = Guid.NewGuid();

        _context.Patients.Add(patient);
        _context.IntakeForms.AddRange(
            new IntakeForm
            {
                Id = olderDraftId,
                PatientId = patient.Id,
                ResponseJson = """{"fullName":"Older Draft"}""",
                PainMapData = "{}",
                Consents = "{}",
                TemplateVersion = "1.0",
                IsLocked = false,
                AccessToken = "older-token",
                LastModifiedUtc = new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc),
                ModifiedByUserId = _userId,
                ClinicId = _clinicId
            },
            new IntakeForm
            {
                Id = submittedId,
                PatientId = patient.Id,
                ResponseJson = """{"fullName":"Latest Locked","painSeverityProvided":true,"painSeverityScore":0}""",
                PainMapData = "{}",
                Consents = "{}",
                TemplateVersion = "1.0",
                IsLocked = true,
                SubmittedAt = new DateTime(2026, 5, 1, 14, 0, 0, DateTimeKind.Utc),
                AccessToken = "submitted-token",
                LastModifiedUtc = new DateTime(2026, 5, 1, 14, 0, 0, DateTimeKind.Utc),
                ModifiedByUserId = _userId,
                ClinicId = _clinicId
            });
        await _context.SaveChangesAsync();

        var latest = await _service.GetLatestByPatientIdAsync(patient.Id);

        Assert.NotNull(latest);
        Assert.Equal(submittedId, latest!.IntakeId);
        Assert.True(latest.IsLocked);
        Assert.True(latest.IsSubmitted);
        Assert.True(latest.PainSeverityProvided);
        Assert.Equal(0, latest.PainSeverityScore);
        Assert.Equal(new DateTime(2026, 5, 1, 14, 0, 0, DateTimeKind.Utc), latest.SubmittedAt);
    }

    [Fact]
    public async Task EnsureDraftAsync_ReturnsLocked_WhenOnlyLockedIntakeExists()
    {
        var patient = CreatePatient("Jordan", "Locked");
        _context.Patients.Add(patient);
        _context.IntakeForms.Add(new IntakeForm
        {
            Id = Guid.NewGuid(),
            PatientId = patient.Id,
            ResponseJson = "{}",
            PainMapData = "{}",
            Consents = "{}",
            TemplateVersion = "1.0",
            IsLocked = true,
            AccessToken = "token",
            LastModifiedUtc = DateTime.UtcNow,
            ModifiedByUserId = _userId,
            ClinicId = _clinicId
        });
        await _context.SaveChangesAsync();

        var result = await _service.EnsureDraftAsync(patient.Id);

        Assert.Equal(IntakeEnsureDraftStatus.Locked, result.Status);
        Assert.Null(result.Draft);
        Assert.Contains("locked", result.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SubmitAsync_LocksDraftAndStampsSubmittedAt()
    {
        var patient = CreatePatient("Sam", "Submit");
        var intakeId = Guid.NewGuid();
        _context.Patients.Add(patient);
        _context.IntakeForms.Add(new IntakeForm
        {
            Id = intakeId,
            PatientId = patient.Id,
            ResponseJson = """{"fullName":"Sam Submit"}""",
            PainMapData = "{}",
            Consents = "{}",
            TemplateVersion = "1.0",
            IsLocked = false,
            AccessToken = "token",
            LastModifiedUtc = new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc),
            ModifiedByUserId = _userId,
            ClinicId = _clinicId
        });
        await _context.SaveChangesAsync();

        await _service.SubmitAsync(new IntakeResponseDraft
        {
            PatientId = patient.Id,
            FullName = "Sam Submit",
            SexAtBirth = "Male",
            AddressLine1 = "400 Beta Street",
            City = "San Diego",
            StateOrProvince = "CA",
            PostalCode = "92101",
            EmergencyContactName = "Emergency Contact",
            EmergencyContactPhone = "555-0110",
            ReferringDoctorName = "Dr. Referral",
            ReferringDoctorNpi = "1234567890",
            InsuranceCompanyName = "PFPT Beta PPO",
            MemberOrPolicyNumber = "BETA001",
            PayerType = "Commercial",
            InsuranceCoverageType = "Primary",
            SecondaryInsuranceCompanyName = "Secondary Health",
            SecondaryMemberOrPolicyNumber = "SEC-123",
            SecondaryGroupNumber = "SEC-GRP",
            AdjusterName = "Alex Adjuster",
            AdjusterPhone = "555-0200",
            AdjusterEmail = "adjuster@example.com",
            AdjusterFax = "555-0201",
            CurrentLevelOfFunction = "Independent in home, limited in community walking endurance.",
            FunctionalLimitations = "Difficulty walking longer than 10 minutes.",
            PainSeverityProvided = false
        });

        var storedDraft = await _context.IntakeForms.SingleAsync(form => form.Id == intakeId);
        Assert.True(storedDraft.IsLocked);
        Assert.NotNull(storedDraft.SubmittedAt);
        Assert.True(storedDraft.LastModifiedUtc >= storedDraft.SubmittedAt.Value);

        using var responseJson = JsonDocument.Parse(storedDraft.ResponseJson);
        Assert.Equal(intakeId, responseJson.RootElement.GetProperty("intakeId").GetGuid());
        Assert.Equal(patient.Id, responseJson.RootElement.GetProperty("patientId").GetGuid());
        Assert.True(responseJson.RootElement.GetProperty("isLocked").GetBoolean());
        Assert.True(responseJson.RootElement.GetProperty("isSubmitted").GetBoolean());
        Assert.Equal(storedDraft.SubmittedAt.Value, responseJson.RootElement.GetProperty("submittedAt").GetDateTime());
        Assert.True(responseJson.RootElement.TryGetProperty("lastModifiedUtc", out var lastModifiedUtc));
        Assert.Equal(JsonValueKind.String, lastModifiedUtc.ValueKind);
        Assert.Equal("Male", responseJson.RootElement.GetProperty("sexAtBirth").GetString());
        Assert.Equal(
            "Independent in home, limited in community walking endurance.",
            responseJson.RootElement.GetProperty("currentLevelOfFunction").GetString());
        Assert.Equal("Difficulty walking longer than 10 minutes.", responseJson.RootElement.GetProperty("functionalLimitations").GetString());

        var submittedPatient = await _context.Patients.SingleAsync(record => record.Id == patient.Id);
        Assert.Equal("400 Beta Street", submittedPatient.AddressLine1);
        Assert.Equal("Dr. Referral", submittedPatient.ReferringPhysician);
        Assert.Equal("1234567890", submittedPatient.PhysicianNpi);
        Assert.Contains("PFPT Beta PPO", submittedPatient.PayerInfoJson, StringComparison.Ordinal);
        using var payerInfoJson = JsonDocument.Parse(submittedPatient.PayerInfoJson);
        Assert.Equal("Commercial", payerInfoJson.RootElement.GetProperty("providerType").GetString());
        Assert.Equal("BETA001", payerInfoJson.RootElement.GetProperty("memberIdPolicyNumber").GetString());
        Assert.Equal("Primary", payerInfoJson.RootElement.GetProperty("insurancePriority").GetString());
        Assert.Equal("Secondary Health", payerInfoJson.RootElement.GetProperty("secondaryInsuranceCompanyName").GetString());
        Assert.Equal("SEC-123", payerInfoJson.RootElement.GetProperty("secondaryMemberIdPolicyNumber").GetString());
        Assert.Equal("SEC-GRP", payerInfoJson.RootElement.GetProperty("secondaryGroupNumber").GetString());
        Assert.Equal("Alex Adjuster", payerInfoJson.RootElement.GetProperty("adjusterName").GetString());
        Assert.Equal("555-0200", payerInfoJson.RootElement.GetProperty("adjusterPhone").GetString());
        Assert.Equal("adjuster@example.com", payerInfoJson.RootElement.GetProperty("adjusterEmail").GetString());
        Assert.Equal("555-0201", payerInfoJson.RootElement.GetProperty("adjusterFax").GetString());
    }

    [Fact]
    public async Task SubmitAsync_PreservesExistingPayerInfo_WhenSubmittedDraftHasNoPayerFields()
    {
        var existingPayerInfo = JsonSerializer.Serialize(new
        {
            PayerType = "Medicare",
            InsuranceCompanyName = "Existing Plan",
            MemberOrPolicyNumber = "EXISTING-001"
        });
        var patient = CreatePatient("Pat", "Payer");
        patient.PayerInfoJson = existingPayerInfo;
        var intakeId = Guid.NewGuid();
        _context.Patients.Add(patient);
        _context.IntakeForms.Add(new IntakeForm
        {
            Id = intakeId,
            PatientId = patient.Id,
            ResponseJson = """{"fullName":"Pat Payer"}""",
            PainMapData = "{}",
            Consents = "{}",
            TemplateVersion = "1.0",
            IsLocked = false,
            AccessToken = "token",
            LastModifiedUtc = new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc),
            ModifiedByUserId = _userId,
            ClinicId = _clinicId
        });
        await _context.SaveChangesAsync();

        await _service.SubmitAsync(new IntakeResponseDraft
        {
            PatientId = patient.Id,
            FullName = "Pat Payer",
            EmailAddress = "pat.payer@example.com",
            PhoneNumber = "555-0113",
            PainSeverityProvided = false
        });

        var submittedPatient = await _context.Patients.SingleAsync(record => record.Id == patient.Id);
        Assert.Equal(existingPayerInfo, submittedPatient.PayerInfoJson);
        Assert.Equal("pat.payer@example.com", submittedPatient.Email);
        Assert.Equal("555-0113", submittedPatient.Phone);
        Assert.Equal(SyncState.Pending, submittedPatient.SyncState);
    }

    [Fact]
    public async Task SubmitAsync_MergesSubmittedPayerInfo_WhenSubmittedDraftHasPartialPayerFields()
    {
        var existingPayerInfo = JsonSerializer.Serialize(new
        {
            PayerType = "Medicare",
            InsuranceCompanyName = "Existing Plan",
            MemberOrPolicyNumber = "EXISTING-001",
            GroupNumber = "GROUP-42",
            SecondaryInsuranceCompanyName = "Existing Secondary",
            SecondaryMemberOrPolicyNumber = "EXISTING-SEC",
            SecondaryGroupNumber = "SEC-GROUP-42",
            AdjusterName = "Existing Adjuster",
            AdjusterPhone = "555-4444",
            CoverageType = "Secondary",
            YearType = "Calendar",
            EffectiveStartDate = "2026-01-01",
            AuthorizationNumber = "AUTH-123",
            VisitsRemaining = "6"
        });
        var patient = CreatePatient("Pat", "Merge");
        patient.PayerInfoJson = existingPayerInfo;
        var intakeId = Guid.NewGuid();
        _context.Patients.Add(patient);
        _context.IntakeForms.Add(new IntakeForm
        {
            Id = intakeId,
            PatientId = patient.Id,
            ResponseJson = """{"fullName":"Pat Merge"}""",
            PainMapData = "{}",
            Consents = "{}",
            TemplateVersion = "1.0",
            IsLocked = false,
            AccessToken = "token",
            LastModifiedUtc = new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc),
            ModifiedByUserId = _userId,
            ClinicId = _clinicId
        });
        await _context.SaveChangesAsync();

        await _service.SubmitAsync(new IntakeResponseDraft
        {
            PatientId = patient.Id,
            FullName = "Pat Merge",
            PayerType = "Commercial",
            PainSeverityProvided = false
        });

        var submittedPatient = await _context.Patients.SingleAsync(record => record.Id == patient.Id);
        using var payerJson = JsonDocument.Parse(submittedPatient.PayerInfoJson);
        Assert.Equal("Commercial", GetJsonPropertyString(payerJson.RootElement, "payerType", "PayerType"));
        Assert.Equal("Commercial", GetJsonPropertyString(payerJson.RootElement, "providerType", "ProviderType"));
        Assert.Equal("Existing Plan", GetJsonPropertyString(payerJson.RootElement, "insuranceCompanyName", "InsuranceCompanyName"));
        Assert.Equal("EXISTING-001", GetJsonPropertyString(payerJson.RootElement, "memberOrPolicyNumber", "MemberOrPolicyNumber"));
        Assert.Equal("EXISTING-001", GetJsonPropertyString(payerJson.RootElement, "memberIdPolicyNumber", "MemberIdPolicyNumber"));
        Assert.Equal("GROUP-42", GetJsonPropertyString(payerJson.RootElement, "groupNumber", "GroupNumber"));
        Assert.Equal("Secondary", GetJsonPropertyString(payerJson.RootElement, "coverageType", "CoverageType"));
        Assert.Equal("Secondary", GetJsonPropertyString(payerJson.RootElement, "insurancePriority", "InsurancePriority"));
        Assert.Equal("Existing Secondary", GetJsonPropertyString(payerJson.RootElement, "secondaryInsuranceCompanyName", "SecondaryInsuranceCompanyName"));
        Assert.Equal("EXISTING-SEC", GetJsonPropertyString(payerJson.RootElement, "secondaryMemberOrPolicyNumber", "SecondaryMemberOrPolicyNumber"));
        Assert.Equal("EXISTING-SEC", GetJsonPropertyString(payerJson.RootElement, "secondaryMemberIdPolicyNumber", "SecondaryMemberIdPolicyNumber"));
        Assert.Equal("SEC-GROUP-42", GetJsonPropertyString(payerJson.RootElement, "secondaryGroupNumber", "SecondaryGroupNumber"));
        Assert.Equal("Existing Adjuster", GetJsonPropertyString(payerJson.RootElement, "adjusterName", "AdjusterName"));
        Assert.Equal("555-4444", GetJsonPropertyString(payerJson.RootElement, "adjusterPhone", "AdjusterPhone"));
        Assert.Equal("Calendar", GetJsonPropertyString(payerJson.RootElement, "YearType"));
        Assert.Equal("2026-01-01", GetJsonPropertyString(payerJson.RootElement, "EffectiveStartDate"));
        Assert.Equal("AUTH-123", GetJsonPropertyString(payerJson.RootElement, "AuthorizationNumber"));
        Assert.Equal("6", GetJsonPropertyString(payerJson.RootElement, "VisitsRemaining"));
    }

    private static string? GetJsonPropertyString(JsonElement element, params string[] propertyNames)
    {
        foreach (var property in element.EnumerateObject())
        {
            foreach (var propertyName in propertyNames)
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    return property.Value.GetString();
                }
            }
        }

        return null;
    }

    [Fact]
    public async Task SubmitAsync_DoesNotOverwriteExistingPhysicianNpi_WhenSubmittedNpiIsInvalid()
    {
        var patient = CreatePatient("Pat", "Npi");
        patient.PhysicianNpi = "1234567890";
        var intakeId = Guid.NewGuid();
        _context.Patients.Add(patient);
        _context.IntakeForms.Add(new IntakeForm
        {
            Id = intakeId,
            PatientId = patient.Id,
            ResponseJson = """{"fullName":"Pat Npi"}""",
            PainMapData = "{}",
            Consents = "{}",
            TemplateVersion = "1.0",
            IsLocked = false,
            AccessToken = "token",
            LastModifiedUtc = new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc),
            ModifiedByUserId = _userId,
            ClinicId = _clinicId
        });
        await _context.SaveChangesAsync();

        await _service.SubmitAsync(new IntakeResponseDraft
        {
            PatientId = patient.Id,
            FullName = "Pat Npi",
            ReferringDoctorNpi = "12ab",
            PainSeverityProvided = false
        });

        var submittedPatient = await _context.Patients.SingleAsync(record => record.Id == patient.Id);
        Assert.Equal("1234567890", submittedPatient.PhysicianNpi);
    }

    [Fact]
    public async Task MarkReviewedAsync_StampsReviewState_AndIsIdempotent()
    {
        var patient = CreatePatient("Rita", "Review");
        var intakeId = Guid.NewGuid();
        var submittedAt = new DateTime(2026, 5, 1, 14, 0, 0, DateTimeKind.Utc);
        _context.Patients.Add(patient);
        _context.IntakeForms.Add(new IntakeForm
        {
            Id = intakeId,
            PatientId = patient.Id,
            ResponseJson = """{"fullName":"Rita Review"}""",
            PainMapData = "{}",
            Consents = "{}",
            TemplateVersion = "1.0",
            IsLocked = true,
            SubmittedAt = submittedAt,
            AccessToken = "review-token",
            LastModifiedUtc = submittedAt,
            ModifiedByUserId = _userId,
            ClinicId = _clinicId
        });
        await _context.SaveChangesAsync();

        var reviewed = await _service.MarkReviewedAsync(intakeId);
        var reviewedAgain = await _service.MarkReviewedAsync(intakeId);

        Assert.True(reviewed.IsLocked);
        Assert.True(reviewed.IsSubmitted);
        Assert.NotNull(reviewed.ReviewedAtUtc);
        Assert.Equal(_userId, reviewed.ReviewedByUserId);
        Assert.Equal(reviewed.ReviewedAtUtc, reviewedAgain.ReviewedAtUtc);

        var stored = await _context.IntakeForms.AsNoTracking().SingleAsync(form => form.Id == intakeId);
        Assert.NotNull(stored.ReviewedAtUtc);
        Assert.Equal(_userId, stored.ReviewedByUserId);
    }

    [Fact]
    public async Task SearchEligiblePatientsAsync_ExcludesLockedOnlyPatients()
    {
        var unlockedPatient = CreatePatient("Una", "Unlocked");
        var newPatient = CreatePatient("Nico", "New");
        var lockedPatient = CreatePatient("Lara", "Locked");

        _context.Patients.AddRange(unlockedPatient, newPatient, lockedPatient);
        _context.IntakeForms.AddRange(
            new IntakeForm
            {
                Id = Guid.NewGuid(),
                PatientId = unlockedPatient.Id,
                ResponseJson = "{}",
                PainMapData = "{}",
                Consents = "{}",
                TemplateVersion = "1.0",
                IsLocked = false,
                AccessToken = "token",
                LastModifiedUtc = DateTime.UtcNow,
                ModifiedByUserId = _userId,
                ClinicId = _clinicId
            },
            new IntakeForm
            {
                Id = Guid.NewGuid(),
                PatientId = lockedPatient.Id,
                ResponseJson = "{}",
                PainMapData = "{}",
                Consents = "{}",
                TemplateVersion = "1.0",
                IsLocked = true,
                AccessToken = "token",
                LastModifiedUtc = DateTime.UtcNow,
                ModifiedByUserId = _userId,
                ClinicId = _clinicId
            });
        await _context.SaveChangesAsync();

        var patients = await _service.SearchEligiblePatientsAsync();

        Assert.Contains(patients, patient => patient.Id == unlockedPatient.Id);
        Assert.Contains(patients, patient => patient.Id == newPatient.Id);
        Assert.DoesNotContain(patients, patient => patient.Id == lockedPatient.Id);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    private Patient CreatePatient(string firstName, string lastName) => new()
    {
        Id = Guid.NewGuid(),
        FirstName = firstName,
        LastName = lastName,
        DateOfBirth = new DateTime(1990, 1, 1),
        ClinicId = _clinicId,
        LastModifiedUtc = DateTime.UtcNow,
        ModifiedByUserId = _userId
    };
}

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
        Assert.True(consentsJson.RootElement.GetProperty("HipaaAcknowledged").GetBoolean());
        Assert.True(consentsJson.RootElement.GetProperty("ConsentToTreat").GetBoolean());
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

using Microsoft.EntityFrameworkCore;
using Moq;
using System.Text.Json;
using PTDoc.Application.DTOs;
using PTDoc.Application.Identity;
using PTDoc.Application.Intake;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;
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
        _service = new IntakeService(
            _context,
            _tenantContext.Object,
            _identityContext.Object,
            new IntakeReferenceDataCatalogService());
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

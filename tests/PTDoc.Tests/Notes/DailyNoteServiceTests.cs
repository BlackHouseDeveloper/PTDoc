using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using PTDoc.Application.AI;
using PTDoc.Application.DTOs;
using PTDoc.Application.Identity;
using PTDoc.Application.ReferenceData;
using PTDoc.Application.Sync;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Compliance;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Notes.Workspace;
using PTDoc.Infrastructure.Outcomes;
using PTDoc.Infrastructure.ReferenceData;
using PTDoc.Infrastructure.Services;
using Xunit;

namespace PTDoc.Tests.Notes;

/// <summary>
/// Unit tests for DailyNoteService covering: upsert-by-date, tenant scoping (ClinicId),
/// signed-note immutability (upsert skips signed notes), CPT billing-unit calculation,
/// medical-necessity checks, FK validation, and AI-first narrative generation.
/// </summary>
[Trait("Category", "CoreCi")]
public class DailyNoteServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _db;
    private readonly Mock<IAiClinicalGenerationService> _aiMock;
    private readonly Mock<ITenantContextAccessor> _tenantMock;
    private readonly Mock<IIdentityContextAccessor> _identityMock;
    private readonly Mock<ISyncEngine> _syncMock;
    private readonly ITreatmentTaxonomyCatalogService _taxonomyCatalog;
    private readonly DailyNoteService _service;

    private static readonly Guid TestClinicId = Guid.NewGuid();
    private static readonly Guid TestUserId = Guid.NewGuid();

    public DailyNoteServiceTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        _tenantMock = new Mock<ITenantContextAccessor>();
        _tenantMock.Setup(x => x.GetCurrentClinicId()).Returns((Guid?)null);

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection, x => x.MigrationsAssembly("PTDoc.Infrastructure.Migrations.Sqlite"))
            .Options;

        _db = new ApplicationDbContext(options, _tenantMock.Object);
        _db.Database.Migrate();

        _aiMock = new Mock<IAiClinicalGenerationService>();
        _identityMock = new Mock<IIdentityContextAccessor>();
        _identityMock.Setup(x => x.GetCurrentUserId()).Returns(TestUserId);
        _syncMock = new Mock<ISyncEngine>();
        _taxonomyCatalog = new TreatmentTaxonomyCatalogService();
        var auditService = new AuditService(_db);
        var rulesEngine = new RulesEngine(_db, auditService);
        var catalogs = new WorkspaceReferenceCatalogService(new OutcomeMeasureRegistry());
        var validationService = new NoteSaveValidationService(_db, rulesEngine, catalogs);

        _service = new DailyNoteService(
            _db,
            _aiMock.Object,
            _tenantMock.Object,
            _identityMock.Object,
            _syncMock.Object,
            _taxonomyCatalog,
            validationService);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<Patient> CreatePatientAsync(string payerType = "Commercial")
    {
        var clinic = new Clinic
        {
            Id = Guid.NewGuid(),
            Name = "Test Clinic",
            Slug = $"test-clinic-{Guid.NewGuid():N}"
        };
        _db.Set<Clinic>().Add(clinic);
        var patient = new Patient
        {
            Id = Guid.NewGuid(),
            ClinicId = clinic.Id,
            FirstName = "Jane",
            LastName = "Doe",
            DateOfBirth = new DateTime(1980, 1, 1),
            PayerInfoJson = JsonSerializer.Serialize(new { PayerType = payerType })
        };
        _db.Patients.Add(patient);
        await _db.SaveChangesAsync();
        return patient;
    }

    private static SaveDailyNoteRequest BuildRequest(Guid patientId, DateTime? date = null) => new()
    {
        PatientId = patientId,
        DateOfService = date ?? DateTime.UtcNow.Date,
        Content = new DailyNoteContentDto
        {
            FocusedActivities = new List<string> { "gait training" },
            CueTypes = new List<int> { 0 },
            CurrentPainScore = 4,
            AssessmentNarrative = "Patient tolerated treatment well.",
            TreatmentTargets = new List<int> { 0 }
        }
    };

    // ── 1. SaveDraftAsync — returns error for non-existent patient ─────────────

    [Fact]
    public async Task SaveDraftAsync_NonExistentPatient_ReturnsError()
    {
        var request = BuildRequest(Guid.NewGuid());

        var (response, error) = await _service.SaveDraftAsync(request);

        Assert.Null(response);
        Assert.NotNull(error);
        Assert.Contains(request.PatientId.ToString(), error);
    }

    // ── 2. SaveDraftAsync — creates note with ClinicId set ────────────────────

    [Fact]
    public async Task SaveDraftAsync_ValidRequest_SetsClinicId()
    {
        _tenantMock.Setup(x => x.GetCurrentClinicId()).Returns(TestClinicId);
        // Create patient with matching ClinicId so it's visible under the tenant filter
        var clinic = new Clinic { Id = TestClinicId, Name = "Tenant Clinic" };
        _db.Set<Clinic>().Add(clinic);
        var patient = new Patient
        {
            Id = Guid.NewGuid(),
            ClinicId = TestClinicId,
            FirstName = "Jane",
            LastName = "Doe",
            DateOfBirth = new DateTime(1980, 1, 1)
        };
        _db.Patients.Add(patient);
        await _db.SaveChangesAsync();
        var request = BuildRequest(patient.Id);

        var (response, error) = await _service.SaveDraftAsync(request);

        Assert.Null(error);
        Assert.NotNull(response);
        var saved = await _db.ClinicalNotes.FindAsync(response!.NoteId);
        Assert.Equal(TestClinicId, saved!.ClinicId);
    }

    // ── 3. SaveDraftAsync — upsert: same patient+date returns updated note ────

    [Fact]
    public async Task SaveDraftAsync_SamePatientAndDate_Upserts()
    {
        var patient = await CreatePatientAsync();
        var date = new DateTime(2026, 3, 30);
        var request = BuildRequest(patient.Id, date);

        var (first, _) = await _service.SaveDraftAsync(request);
        Assert.NotNull(first);

        request.Content.CurrentPainScore = 7;
        var (second, _) = await _service.SaveDraftAsync(request);
        Assert.NotNull(second);

        Assert.Equal(first!.NoteId, second!.NoteId);
        var count = _db.ClinicalNotes.Count(n => n.PatientId == patient.Id && n.NoteType == NoteType.Daily);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task SaveDraftAsync_KnownTreatmentTaxonomySelection_NormalizesCanonicalLabels()
    {
        var patient = await CreatePatientAsync();
        var request = BuildRequest(patient.Id);
        request.Content.TreatmentTaxonomySelections =
        [
            new TreatmentTaxonomySelectionDto
            {
                CategoryId = "foot-ankle",
                ItemId = "talocrural-joint-arthrokinematics",
                CategoryTitle = "client text should be replaced",
                ItemLabel = "client text should be replaced"
            }
        ];

        var (response, error) = await _service.SaveDraftAsync(request);

        Assert.Null(error);
        Assert.NotNull(response);
        var selection = Assert.Single(response!.Content.TreatmentTaxonomySelections);
        Assert.Equal("Foot & Ankle", selection.CategoryTitle);
        Assert.Equal("Talocrural joint arthrokinematics (e.g., posterior glide of talus)", selection.ItemLabel);
    }

    [Fact]
    public async Task SaveDraftAsync_UnknownTreatmentTaxonomySelection_ReturnsError()
    {
        var patient = await CreatePatientAsync();
        var request = BuildRequest(patient.Id);
        request.Content.TreatmentTaxonomySelections =
        [
            new TreatmentTaxonomySelectionDto
            {
                CategoryId = "foot-ankle",
                ItemId = "unknown-item"
            }
        ];

        var (response, error) = await _service.SaveDraftAsync(request);

        Assert.Null(response);
        Assert.Contains("Unknown treatment taxonomy selection", error);
    }

    // ── 4. SaveDraftAsync — sets LastModifiedUtc and enqueues sync ─────────────

    [Fact]
    public async Task SaveDraftAsync_Create_EnqueuesSyncCreate()
    {
        var patient = await CreatePatientAsync();
        var request = BuildRequest(patient.Id);

        var before = DateTime.UtcNow.AddSeconds(-1);
        var (response, _) = await _service.SaveDraftAsync(request);

        var saved = await _db.ClinicalNotes.FindAsync(response!.NoteId);
        Assert.True(saved!.LastModifiedUtc >= before);
        Assert.Equal(TestUserId, saved.ModifiedByUserId);

        _syncMock.Verify(s => s.EnqueueAsync("ClinicalNote", response.NoteId, SyncOperation.Create, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveDraftAsync_Update_EnqueuesSyncUpdate()
    {
        var patient = await CreatePatientAsync();
        var date = new DateTime(2026, 3, 30);
        var request = BuildRequest(patient.Id, date);

        var (first, _) = await _service.SaveDraftAsync(request);
        _syncMock.Reset();

        var (_, _) = await _service.SaveDraftAsync(request);

        _syncMock.Verify(s => s.EnqueueAsync("ClinicalNote", first!.NoteId, SyncOperation.Update, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── 5. SaveDraftAsync — signed note is not overwritten (immutability) ─────

    [Fact]
    public async Task SaveDraftAsync_SignedNote_ReturnsErrorAndDoesNotCreateNewDraft()
    {
        var patient = await CreatePatientAsync();
        var date = new DateTime(2026, 3, 30);

        // Create and sign a note directly in the DB
        var signedNote = new ClinicalNote
        {
            PatientId = patient.Id,
            NoteType = NoteType.Daily,
            DateOfService = date,
            ContentJson = "{}",
            CptCodesJson = "[]",
            SignedUtc = DateTime.UtcNow,
            SignatureHash = "signed"
        };
        _db.ClinicalNotes.Add(signedNote);
        await _db.SaveChangesAsync();

        // SaveDraft should reject any same-day edit attempt once the primary note is finalized.
        var request = BuildRequest(patient.Id, date);
        var (response, error) = await _service.SaveDraftAsync(request);

        Assert.Null(response);
        Assert.Equal("Signed notes cannot be modified. Create addendum.", error);
        Assert.Equal(1, _db.ClinicalNotes.Count(n => n.PatientId == patient.Id && n.NoteType == NoteType.Daily));
    }

    [Fact]
    public async Task GetByTaxonomyAsync_ExcludesAddendumNotes()
    {
        var patient = await CreatePatientAsync();
        var request = BuildRequest(patient.Id, new DateTime(2026, 4, 1));
        request.Content.TreatmentTaxonomySelections =
        [
            new TreatmentTaxonomySelectionDto
            {
                CategoryId = "foot-ankle",
                ItemId = "talocrural-joint-arthrokinematics"
            }
        ];

        var primaryResult = await _service.SaveDraftAsync(request);
        Assert.NotNull(primaryResult.DailyNote);

        var addendum = new ClinicalNote
        {
            PatientId = patient.Id,
            ParentNoteId = primaryResult.DailyNote!.NoteId,
            IsAddendum = true,
            NoteType = NoteType.Daily,
            DateOfService = request.DateOfService,
            ContentJson = "{}",
            CptCodesJson = "[]",
            LastModifiedUtc = DateTime.UtcNow
        };
        _db.ClinicalNotes.Add(addendum);
        _db.NoteTaxonomySelections.Add(new NoteTaxonomySelection
        {
            ClinicalNoteId = addendum.Id,
            CategoryId = "foot-ankle",
            CategoryTitle = "Foot & Ankle",
            CategoryKind = 0,
            ItemId = "talocrural-joint-arthrokinematics",
            ItemLabel = "Talocrural joint arthrokinematics (e.g., posterior glide of talus)"
        });
        await _db.SaveChangesAsync();

        var results = await _service.GetByTaxonomyAsync("foot-ankle");

        Assert.Single(results);
        Assert.Equal(primaryResult.DailyNote.NoteId, results[0].NoteId);
    }

    [Fact]
    public async Task SaveDraftAsync_PnHardStop_ReturnsStructuredErrorAndDoesNotPersist()
    {
        var patient = await CreatePatientAsync("Medicare");
        await SeedSignedEvaluationAsync(patient.Id, new DateTime(2026, 3, 1));
        await SeedDailyNotesAsync(patient.Id, new DateTime(2026, 3, 2), 10);

        var request = BuildRequest(patient.Id, new DateTime(2026, 4, 3));

        var result = await _service.SaveDraftAsync(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Contains("Progress Note required", StringComparison.OrdinalIgnoreCase));
        Assert.Null(result.DailyNote);
        Assert.Equal(11, _db.ClinicalNotes.Count(n => n.PatientId == patient.Id));
    }

    [Fact]
    public async Task SaveDraftAsync_MissingTimedMinutes_ReturnsWarningAndPersistsDraft()
    {
        var patient = await CreatePatientAsync("Medicare");
        var request = BuildRequest(patient.Id);
        request.Content.CptCodes =
        [
            new CptCodeEntryDto
            {
                Code = "97110",
                Units = 2
            }
        ];

        var result = await _service.SaveDraftAsync(request);

        Assert.True(result.IsValid);
        Assert.NotNull(result.DailyNote);
        Assert.Contains(result.Warnings, warning => warning.Contains("Timed CPT minutes are missing", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SaveDraftAsync_LessThanFiveTimedMinutes_BlocksPersistence()
    {
        var patient = await CreatePatientAsync("Medicare");
        var request = BuildRequest(patient.Id);
        request.Content.CptCodes =
        [
            new CptCodeEntryDto
            {
                Code = "97110",
                Units = 1,
                Minutes = 4
            }
        ];

        var result = await _service.SaveDraftAsync(request);

        Assert.False(result.IsValid);
        Assert.Contains("Minimum 5 minutes required", result.Errors);
        Assert.Null(result.DailyNote);
    }

    // ── 6. CalculateCptTime — 8-minute rule ───────────────────────────────────

    [Theory]
    [InlineData(8, 1)]
    [InlineData(15, 1)]
    [InlineData(22, 1)]
    [InlineData(23, 2)]
    [InlineData(30, 2)]
    [InlineData(37, 2)]
    [InlineData(38, 3)]
    public void CalculateCptTime_EightMinuteRule_ReturnsCorrectUnits(int minutes, int expectedUnits)
    {
        var request = new CptTimeCalculationRequest
        {
            CptCodes = new List<CptCodeEntryDto> { new() { Code = "97110", Minutes = minutes } }
        };

        var result = _service.CalculateCptTime(request);

        Assert.Equal(expectedUnits, result.TotalBillingUnits);
        Assert.Equal(minutes, result.TotalMinutes);
    }

    [Fact]
    public void CalculateCptTime_MultipleCodes_SumsTotals()
    {
        var request = new CptTimeCalculationRequest
        {
            CptCodes = new List<CptCodeEntryDto>
            {
                new() { Code = "97110", Minutes = 23 }, // 2 units
                new() { Code = "97140", Minutes = 15 }  // 1 unit
            }
        };

        var result = _service.CalculateCptTime(request);

        Assert.Equal(3, result.TotalBillingUnits);
        Assert.Equal(38, result.TotalMinutes);
    }

    [Fact]
    public void CalculateCptTime_AggregatesAcrossTimedCodes()
    {
        var request = new CptTimeCalculationRequest
        {
            CptCodes = new List<CptCodeEntryDto>
            {
                new() { Code = "97110", Minutes = 8 },
                new() { Code = "97140", Minutes = 8 }
            }
        };

        var result = _service.CalculateCptTime(request);

        Assert.Equal(1, result.TotalBillingUnits);
        Assert.Equal(16, result.TotalMinutes);
    }

    // ── 7. CheckMedicalNecessity — passes when all required fields present ────

    [Fact]
    public void CheckMedicalNecessity_AllRequiredFieldsPresent_Passes()
    {
        var content = new DailyNoteContentDto
        {
            FocusedActivities = new List<string> { "gait training" },
            CueTypes = new List<int> { 0 },
            CurrentPainScore = 4,
            AssessmentNarrative = "Patient tolerated treatment well.",
            TreatmentTargets = new List<int> { 0 },
            CptCodes = new List<CptCodeEntryDto> { new() { Code = "97110", Minutes = 15 } },
            PlanDirection = 0
        };

        var result = _service.CheckMedicalNecessity(content);

        Assert.True(result.Passes);
        Assert.Empty(result.MissingElements);
    }

    // ── 8. CheckMedicalNecessity — fails when required fields missing ──────────

    [Fact]
    public void CheckMedicalNecessity_EmptyContent_FailsWithMissingElements()
    {
        var content = new DailyNoteContentDto();

        var result = _service.CheckMedicalNecessity(content);

        Assert.False(result.Passes);
        Assert.NotEmpty(result.MissingElements);
    }

    [Fact]
    public void CheckMedicalNecessity_MissingClinicalReasoning_FailsWithReasoningElement()
    {
        var content = new DailyNoteContentDto
        {
            FocusedActivities = new List<string> { "balance training" },
            CueTypes = new List<int> { 0 },
            CurrentPainScore = 3,
            TreatmentTargets = new List<int> { 0 }
            // No AssessmentNarrative / ClinicalInterpretation
        };

        var result = _service.CheckMedicalNecessity(content);

        Assert.False(result.Passes);
        Assert.Contains(result.MissingElements, m => m.Contains("Clinical reasoning"));
    }

    // ── 9. GenerateAssessmentNarrativeAsync — AI-first, template fallback ──────

    [Fact]
    public async Task GenerateAssessmentNarrativeAsync_AiSucceeds_ReturnsAiNarrative()
    {
        var aiNarrative = "AI-generated assessment narrative.";
        _aiMock.Setup(x => x.GenerateAssessmentAsync(It.IsAny<AssessmentGenerationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AssessmentGenerationResult
            {
                GeneratedText = aiNarrative,
                Confidence = 0.9,
                Success = true,
                SourceInputs = new AssessmentGenerationRequest { NoteId = Guid.Empty, ChiefComplaint = "test", IsNoteSigned = false }
            });

        var content = new DailyNoteContentDto { FocusedActivities = new List<string> { "gait training" } };
        var result = await _service.GenerateAssessmentNarrativeAsync(content);

        Assert.Equal(aiNarrative, result);
    }

    private async Task SeedSignedEvaluationAsync(Guid patientId, DateTime dateOfService)
    {
        _db.ClinicalNotes.Add(new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = patientId,
            NoteType = NoteType.Evaluation,
            DateOfService = dateOfService,
            SignatureHash = "signed",
            SignedUtc = dateOfService.AddHours(1),
            LastModifiedUtc = dateOfService
        });
        await _db.SaveChangesAsync();
    }

    private async Task SeedDailyNotesAsync(Guid patientId, DateTime startDate, int count)
    {
        for (var index = 0; index < count; index++)
        {
            _db.ClinicalNotes.Add(new ClinicalNote
            {
                Id = Guid.NewGuid(),
                PatientId = patientId,
                NoteType = NoteType.Daily,
                DateOfService = startDate.AddDays(index),
                LastModifiedUtc = startDate.AddDays(index)
            });
        }

        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task GenerateAssessmentNarrativeAsync_AiFails_ReturnsTemplateFallback()
    {
        _aiMock.Setup(x => x.GenerateAssessmentAsync(It.IsAny<AssessmentGenerationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AssessmentGenerationResult
            {
                GeneratedText = string.Empty,
                Confidence = 0,
                Success = false,
                ErrorMessage = "AI unavailable",
                SourceInputs = new AssessmentGenerationRequest { NoteId = Guid.Empty, ChiefComplaint = "test", IsNoteSigned = false }
            });

        var content = new DailyNoteContentDto { FocusedActivities = new List<string> { "balance training" } };
        var result = await _service.GenerateAssessmentNarrativeAsync(content);

        // Falls back to template — must mention the focused activity
        Assert.Contains("balance training", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GenerateAssessmentNarrativeAsync_AiThrows_ReturnsTemplateFallback()
    {
        _aiMock.Setup(x => x.GenerateAssessmentAsync(It.IsAny<AssessmentGenerationRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("connection refused"));

        var content = new DailyNoteContentDto { FocusedActivities = new List<string> { "therapeutic exercise" } };
        var result = await _service.GenerateAssessmentNarrativeAsync(content);

        Assert.Contains("therapeutic exercise", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GenerateAssessmentNarrativeAsync_Cancelled_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        _aiMock.Setup(x => x.GenerateAssessmentAsync(It.IsAny<AssessmentGenerationRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var content = new DailyNoteContentDto { FocusedActivities = new List<string> { "gait" } };

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _service.GenerateAssessmentNarrativeAsync(content, cts.Token));
    }

    // ── 10. GetByIdAsync — returns null for unknown note ─────────────────────

    [Fact]
    public async Task GetByIdAsync_UnknownId_ReturnsNull()
    {
        var result = await _service.GetByIdAsync(Guid.NewGuid());
        Assert.Null(result);
    }

    // ── 11. GetEvalCarryForwardAsync — returns empty when no eval note ─────────

    [Fact]
    public async Task GetEvalCarryForwardAsync_NoEvalNote_ReturnsEmptyActivities()
    {
        var patient = await CreatePatientAsync();

        var result = await _service.GetEvalCarryForwardAsync(patient.Id);

        Assert.Equal(patient.Id, result.PatientId);
        Assert.Empty(result.Activities);
        Assert.Null(result.EvalNoteId);
    }

    // ── 12. NoteTaxonomySelections join-table persistence ────────────────────

    [Fact]
    public async Task SaveDraftAsync_WithTaxonomySelections_PersistsJoinTableRows()
    {
        var patient = await CreatePatientAsync();
        var request = BuildRequest(patient.Id);
        request.Content.TreatmentTaxonomySelections =
        [
            new TreatmentTaxonomySelectionDto
            {
                CategoryId = "foot-ankle",
                ItemId = "talocrural-joint-arthrokinematics",
                CategoryTitle = "ignore",
                ItemLabel = "ignore"
            }
        ];

        var (response, error) = await _service.SaveDraftAsync(request);

        Assert.Null(error);
        Assert.NotNull(response);

        var rows = _db.NoteTaxonomySelections
            .Where(s => s.ClinicalNoteId == response!.NoteId)
            .ToList();

        var row = Assert.Single(rows);
        Assert.Equal("foot-ankle", row.CategoryId);
        Assert.Equal("talocrural-joint-arthrokinematics", row.ItemId);
        Assert.Equal("Foot & Ankle", row.CategoryTitle);
        Assert.Equal("Talocrural joint arthrokinematics (e.g., posterior glide of talus)", row.ItemLabel);
    }

    [Fact]
    public async Task SaveDraftAsync_UpdateNote_ReplacesExistingJoinTableRows()
    {
        var patient = await CreatePatientAsync();
        var date = new DateTime(2026, 3, 30);
        var request = BuildRequest(patient.Id, date);
        request.Content.TreatmentTaxonomySelections =
        [
            new TreatmentTaxonomySelectionDto
            {
                CategoryId = "foot-ankle",
                ItemId = "talocrural-joint-arthrokinematics",
                CategoryTitle = "x",
                ItemLabel = "x"
            }
        ];
        var (first, _) = await _service.SaveDraftAsync(request);
        Assert.NotNull(first);

        // Update with a different selection
        request.Content.TreatmentTaxonomySelections =
        [
            new TreatmentTaxonomySelectionDto
            {
                CategoryId = "knee",
                ItemId = "quad-control-activation",
                CategoryTitle = "x",
                ItemLabel = "x"
            }
        ];
        var (second, error) = await _service.SaveDraftAsync(request);

        Assert.Null(error);
        Assert.NotNull(second);
        Assert.Equal(first!.NoteId, second!.NoteId); // same note updated

        var rows = _db.NoteTaxonomySelections
            .Where(s => s.ClinicalNoteId == second.NoteId)
            .ToList();

        var row = Assert.Single(rows);
        Assert.Equal("knee", row.CategoryId);
        Assert.Equal("quad-control-activation", row.ItemId);
    }

    // ── 13. GetByTaxonomyAsync — first-class taxonomy filter queries ──────────

    [Fact]
    public async Task GetByTaxonomyAsync_FiltersByCategory_ReturnsMatchingNotes()
    {
        var patient = await CreatePatientAsync();

        // Note 1: has foot-ankle selection
        var r1 = BuildRequest(patient.Id, new DateTime(2026, 3, 28));
        r1.Content.TreatmentTaxonomySelections =
        [
            new TreatmentTaxonomySelectionDto
            {
                CategoryId = "foot-ankle",
                ItemId = "talocrural-joint-arthrokinematics",
                CategoryTitle = "x",
                ItemLabel = "x"
            }
        ];
        await _service.SaveDraftAsync(r1);

        // Note 2: no taxonomy — should not be returned
        var r2 = BuildRequest(patient.Id, new DateTime(2026, 3, 29));
        await _service.SaveDraftAsync(r2);

        var results = await _service.GetByTaxonomyAsync("foot-ankle");

        Assert.Single(results);
        Assert.Equal(new DateTime(2026, 3, 28), results[0].DateOfService.Date);
    }

    [Fact]
    public async Task GetByTaxonomyAsync_FiltersByCategoryAndItem_ReturnsOnlyExactMatch()
    {
        var patient = await CreatePatientAsync();

        // Note 1: foot-ankle / talocrural-joint-arthrokinematics
        var r1 = BuildRequest(patient.Id, new DateTime(2026, 3, 27));
        r1.Content.TreatmentTaxonomySelections =
        [
            new TreatmentTaxonomySelectionDto
            {
                CategoryId = "foot-ankle",
                ItemId = "talocrural-joint-arthrokinematics",
                CategoryTitle = "x",
                ItemLabel = "x"
            }
        ];
        await _service.SaveDraftAsync(r1);

        // Note 2: foot-ankle / subtalar-joint-arthrokinematics — same category, different item
        var r2 = BuildRequest(patient.Id, new DateTime(2026, 3, 28));
        r2.Content.TreatmentTaxonomySelections =
        [
            new TreatmentTaxonomySelectionDto
            {
                CategoryId = "foot-ankle",
                ItemId = "subtalar-joint-arthrokinematics",
                CategoryTitle = "x",
                ItemLabel = "x"
            }
        ];
        await _service.SaveDraftAsync(r2);

        var results = await _service.GetByTaxonomyAsync("foot-ankle", itemId: "talocrural-joint-arthrokinematics");

        Assert.Single(results);
        Assert.Equal(new DateTime(2026, 3, 27), results[0].DateOfService.Date);
    }

    [Fact]
    public async Task GetByTaxonomyAsync_UnknownCategory_ReturnsEmpty()
    {
        var patient = await CreatePatientAsync();
        var request = BuildRequest(patient.Id);
        await _service.SaveDraftAsync(request);

        var results = await _service.GetByTaxonomyAsync("nonexistent-category");

        Assert.Empty(results);
    }

    [Fact]
    public async Task GetByTaxonomyAsync_WithPatientFilter_ScopesToPatient()
    {
        var patient1 = await CreatePatientAsync();
        var patient2 = await CreatePatientAsync();

        var selectionEntry = new TreatmentTaxonomySelectionDto
        {
            CategoryId = "foot-ankle",
            ItemId = "talocrural-joint-arthrokinematics",
            CategoryTitle = "x",
            ItemLabel = "x"
        };

        var r1 = BuildRequest(patient1.Id, new DateTime(2026, 3, 28));
        r1.Content.TreatmentTaxonomySelections = [selectionEntry];
        await _service.SaveDraftAsync(r1);

        var r2 = BuildRequest(patient2.Id, new DateTime(2026, 3, 28));
        r2.Content.TreatmentTaxonomySelections = [selectionEntry];
        await _service.SaveDraftAsync(r2);

        var results = await _service.GetByTaxonomyAsync("foot-ankle", patientId: patient1.Id);

        Assert.Single(results);
        Assert.Equal(patient1.Id, results[0].PatientId);
    }
}

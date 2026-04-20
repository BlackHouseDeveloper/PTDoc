using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PTDoc.Application.Identity;
using PTDoc.Application.Intake;
using PTDoc.Application.Notes.Content;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Compliance;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Data.Seeders;
using PTDoc.Infrastructure.Notes.Workspace;
using PTDoc.Infrastructure.Outcomes;
using PTDoc.Infrastructure.ReferenceData;
using PTDoc.Infrastructure.Services;
using Xunit;

namespace PTDoc.Tests.Integration;

[Trait("Category", "CoreCi")]
public sealed class DatabaseSeederQaFixtureTests : IDisposable
{
    private static readonly Guid HistoricalVasPatientId = Guid.Parse("5f2d7a29-3c5f-4a0c-9c6b-2d45c8f78a31");
    private static readonly Guid HistoricalVasNoteId = Guid.Parse("8d0c3d47-5e33-46a4-9c6d-6d6c01d7e8f4");
    private static readonly Guid HistoricalVasOutcomeMeasureResultId = Guid.Parse("2b80b173-53b2-4d92-b017-6cfa52aca5c1");
    private static readonly Guid SubmittedShoulderPatientId = Guid.Parse("c4d1f4e9-f5a5-4ccb-b92a-4c1a1a6ce7d2");
    private static readonly Guid SubmittedShoulderIntakeFormId = Guid.Parse("6bdb789b-a4c5-41c2-9a4b-bf6173f0d4c8");

    private readonly ApplicationDbContext _context;

    public DatabaseSeederQaFixtureTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"DatabaseSeederQaFixture_{Guid.NewGuid()}")
            .Options;

        _context = new ApplicationDbContext(options);
    }

    [Fact]
    public async Task SeedTestDataAsync_CreatesQaFixturesIdempotently()
    {
        await DatabaseSeeder.SeedTestDataAsync(_context, NullLogger.Instance);
        await DatabaseSeeder.SeedTestDataAsync(_context, NullLogger.Instance);

        var historicalPatient = await _context.Patients.SingleAsync(patient => patient.Id == HistoricalVasPatientId);
        var historicalNote = await _context.ClinicalNotes.SingleAsync(note => note.Id == HistoricalVasNoteId);
        var historicalOutcome = await _context.OutcomeMeasureResults.SingleAsync(result => result.Id == HistoricalVasOutcomeMeasureResultId);
        var shoulderIntake = await _context.IntakeForms.SingleAsync(form => form.Id == SubmittedShoulderIntakeFormId);

        Assert.Equal(DatabaseSeeder.DefaultClinicId, historicalPatient.ClinicId);
        Assert.Equal(HistoricalVasPatientId, historicalNote.PatientId);
        Assert.Equal(OutcomeMeasureType.VAS, historicalOutcome.MeasureType);
        Assert.Equal(5d, historicalOutcome.Score);
        Assert.Equal(HistoricalVasNoteId, historicalOutcome.NoteId);

        Assert.Equal(SubmittedShoulderPatientId, shoulderIntake.PatientId);
        Assert.Equal(DatabaseSeeder.DefaultClinicId, shoulderIntake.ClinicId);
        Assert.Equal("1.0", shoulderIntake.TemplateVersion);
        Assert.True(shoulderIntake.SubmittedAt.HasValue);
        Assert.True(shoulderIntake.IsLocked);

        using var responseJson = JsonDocument.Parse(shoulderIntake.ResponseJson);
        if (responseJson.RootElement.TryGetProperty("recommendedOutcomeMeasures", out var recommended))
        {
            Assert.Equal(0, recommended.GetArrayLength());
        }

        if (responseJson.RootElement.TryGetProperty("structuredData", out var responseStructuredData))
        {
            Assert.Equal("shoulder", responseStructuredData.GetProperty("bodyPartSelections")[0].GetProperty("bodyPartId").GetString());
            Assert.Equal("right", responseStructuredData.GetProperty("bodyPartSelections")[0].GetProperty("lateralities")[0].GetString());
        }

        using var structuredJson = JsonDocument.Parse(shoulderIntake.StructuredDataJson!);
        Assert.Equal("2026-03-30", structuredJson.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal("shoulder", structuredJson.RootElement.GetProperty("bodyPartSelections")[0].GetProperty("bodyPartId").GetString());
        Assert.Equal("right", structuredJson.RootElement.GetProperty("bodyPartSelections")[0].GetProperty("lateralities")[0].GetString());

        Assert.Equal(1, await _context.Patients.CountAsync(patient => patient.Id == HistoricalVasPatientId));
        Assert.Equal(1, await _context.ClinicalNotes.CountAsync(note => note.Id == HistoricalVasNoteId));
        Assert.Equal(1, await _context.OutcomeMeasureResults.CountAsync(result => result.Id == HistoricalVasOutcomeMeasureResultId));
        Assert.Equal(1, await _context.IntakeForms.CountAsync(form => form.Id == SubmittedShoulderIntakeFormId));
    }

    [Fact]
    public async Task SeedTestDataAsync_SubmittedShoulderFixtureProducesExpectedEvaluationSeed()
    {
        await DatabaseSeeder.SeedTestDataAsync(_context, NullLogger.Instance);

        var service = CreateWorkspaceService();

        var seed = await service.GetEvaluationSeedAsync(SubmittedShoulderPatientId);

        Assert.NotNull(seed);
        Assert.True(seed!.FromLockedSubmittedIntake);
        Assert.Equal(SubmittedShoulderIntakeFormId, seed.SourceIntakeId);
        Assert.Equal(BodyPart.Shoulder, seed.Payload.Objective.PrimaryBodyPart);
        Assert.Equal(
            ["DASH", "NPRS", "PSFS", "QuickDASH"],
            seed.Payload.Objective.RecommendedOutcomeMeasures.OrderBy(value => value).ToArray());
        Assert.DoesNotContain(seed.Payload.Objective.RecommendedOutcomeMeasures, value => string.Equals(value, "VAS", StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }

    private NoteWorkspaceV2Service CreateWorkspaceService()
    {
        var registry = new OutcomeMeasureRegistry();
        var intakeReferenceData = new IntakeReferenceDataCatalogService();
        var intakeBodyPartMapper = new IntakeBodyPartMapper(intakeReferenceData);
        var intakeDraftCanonicalizer = new IntakeDraftCanonicalizer(registry, intakeBodyPartMapper);
        var catalogs = new WorkspaceReferenceCatalogService(registry);
        var auditService = new AuditService(_context);
        var rulesEngine = new RulesEngine(_context, auditService);
        var validationService = new NoteSaveValidationService(_context, rulesEngine, catalogs);
        var carryForwardService = new CarryForwardService(_context);

        return new NoteWorkspaceV2Service(
            _context,
            new SeederIdentityContextAccessor(),
            new SeederTenantContextAccessor(),
            validationService,
            new PlanOfCareCalculator(),
            new AssessmentCompositionService(),
            new GoalManagementService(catalogs),
            registry,
            intakeReferenceData,
            intakeBodyPartMapper,
            intakeDraftCanonicalizer,
            carryForwardService);
    }

    private sealed class SeederIdentityContextAccessor : IIdentityContextAccessor
    {
        private static readonly Guid UserId = Guid.Parse("00000000-0000-0000-0000-000000000111");

        public Guid GetCurrentUserId() => UserId;
        public Guid? TryGetCurrentUserId() => UserId;
        public string GetCurrentUsername() => "database-seeder-test";
        public string? GetCurrentUserRole() => Roles.PT;
    }

    private sealed class SeederTenantContextAccessor : ITenantContextAccessor
    {
        public Guid? GetCurrentClinicId() => DatabaseSeeder.DefaultClinicId;
    }
}

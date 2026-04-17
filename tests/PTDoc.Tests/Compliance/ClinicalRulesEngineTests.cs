using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Compliance;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Compliance;
using PTDoc.Infrastructure.Data;
using System.Text.Json;
using Xunit;

namespace PTDoc.Tests.Compliance;

/// <summary>
/// Sprint N: Tests for the clinical rules engine.
/// Covers documentation completeness, blocking rule enforcement,
/// Medicare compliance rules, and note signing validation.
/// </summary>
[Trait("Category", "Compliance")]
public class ClinicalRulesEngineTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly ClinicalRulesEngine _engine;

    public ClinicalRulesEngineTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"ClinicalRulesDb_{Guid.NewGuid()}")
            .Options;

        _context = new ApplicationDbContext(options);
        _engine = new ClinicalRulesEngine(_context);
    }

    // ─── Note not found ───────────────────────────────────────────────────────

    [Fact]
    public async Task RunClinicalValidation_NoteNotFound_ReturnsBlockingError()
    {
        var result = await _engine.RunClinicalValidationAsync(Guid.NewGuid());

        Assert.Single(result);
        Assert.True(result[0].Blocking);
        Assert.Equal("NOTE_NOT_FOUND", result[0].RuleId);
        Assert.Equal(ValidationSeverity.Error, result[0].Severity);
    }

    // ─── Invalid ContentJson ──────────────────────────────────────────────────

    [Fact]
    public async Task RunClinicalValidation_MalformedContentJson_BlockingInvalidContentError()
    {
        var note = AddNote(NoteType.Daily, "NOT_VALID_JSON{{{{");

        var result = await _engine.RunClinicalValidationAsync(note.Id);

        var rule = result.FirstOrDefault(r => r.RuleId == "INVALID_CONTENT_JSON");
        Assert.NotNull(rule);
        Assert.True(rule.Blocking);
        Assert.Equal(ValidationSeverity.Error, rule.Severity);
        Assert.Equal(RuleCategory.DocCompleteness, rule.Category);
    }

    // ─── Documentation completeness: objective measures ───────────────────────

    [Fact]
    public async Task RunClinicalValidation_EvalWithNoObjectiveMetrics_BlockingError()
    {
        var note = AddNote(NoteType.Evaluation, "{}");

        var result = await _engine.RunClinicalValidationAsync(note.Id);

        var rule = result.FirstOrDefault(r => r.RuleId == "DOC_OBJECTIVE");
        Assert.NotNull(rule);
        Assert.True(rule.Blocking);
        Assert.Equal(ValidationSeverity.Error, rule.Severity);
        Assert.Equal(RuleCategory.DocCompleteness, rule.Category);
    }

    [Fact]
    public async Task RunClinicalValidation_DailyWithNoObjectiveMetrics_NonBlockingWarning()
    {
        var note = AddNote(NoteType.Daily, "{}");

        var result = await _engine.RunClinicalValidationAsync(note.Id);

        var rule = result.FirstOrDefault(r => r.RuleId == "DOC_OBJECTIVE");
        Assert.NotNull(rule);
        Assert.False(rule.Blocking);
        Assert.Equal(ValidationSeverity.Warning, rule.Severity);
    }

    [Fact]
    public async Task RunClinicalValidation_EvalWithObjectiveMetrics_NoObjectiveRule()
    {
        var note = AddNote(NoteType.Evaluation, "{}");
        _context.ObjectiveMetrics.Add(new ObjectiveMetric
        {
            Id = Guid.NewGuid(),
            NoteId = note.Id,
            BodyPart = BodyPart.Lumbar,
            MetricType = MetricType.ROM,
            Value = "75 degrees"
        });
        await _context.SaveChangesAsync();

        var result = await _engine.RunClinicalValidationAsync(note.Id);

        Assert.DoesNotContain(result, r => r.RuleId == "DOC_OBJECTIVE");
    }

    [Fact]
    public async Task RunClinicalValidation_EvalWithStructuredObjectiveFindings_NoObjectiveRule()
    {
        var payload = new NoteWorkspaceV2Payload
        {
            NoteType = NoteType.Evaluation,
            Objective = new WorkspaceObjectiveV2
            {
                GaitObservation = new GaitObservationV2
                {
                    PrimaryPattern = "antalgic",
                    Deviations = ["Decreased stride length"],
                    AdditionalObservations = "Reduced stance time on the right."
                }
            }
        };

        var note = AddNote(
            NoteType.Evaluation,
            JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        var result = await _engine.RunClinicalValidationAsync(note.Id);

        Assert.DoesNotContain(result, r => r.RuleId == "DOC_OBJECTIVE");
    }

    [Fact]
    public async Task RunClinicalValidation_EvalWithOnlyRecommendedOutcomeMeasures_StillRequiresObjectiveRule()
    {
        var payload = new NoteWorkspaceV2Payload
        {
            NoteType = NoteType.Evaluation,
            Objective = new WorkspaceObjectiveV2
            {
                RecommendedOutcomeMeasures = ["NPRS/VAS", "PSFS"]
            }
        };

        var note = AddNote(
            NoteType.Evaluation,
            JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        var result = await _engine.RunClinicalValidationAsync(note.Id);

        var rule = result.FirstOrDefault(r => r.RuleId == "DOC_OBJECTIVE");
        Assert.NotNull(rule);
        Assert.True(rule.Blocking);
    }

    // ─── Documentation completeness: goals ────────────────────────────────────

    [Fact]
    public async Task RunClinicalValidation_EvalWithNoGoals_BlockingError()
    {
        var note = AddNote(NoteType.Evaluation, "{}");

        var result = await _engine.RunClinicalValidationAsync(note.Id);

        var rule = result.FirstOrDefault(r => r.RuleId == "DOC_GOALS");
        Assert.NotNull(rule);
        Assert.True(rule.Blocking);
        Assert.Equal(ValidationSeverity.Error, rule.Severity);
    }

    [Fact]
    public async Task RunClinicalValidation_EvalWithGoals_NoGoalsRule()
    {
        var note = AddNote(NoteType.Evaluation,
            """{"goals": ["Patient will walk 100ft without assist"]}""");

        var result = await _engine.RunClinicalValidationAsync(note.Id);

        Assert.DoesNotContain(result, r => r.RuleId == "DOC_GOALS");
    }

    [Fact]
    public async Task RunClinicalValidation_DailyWithNoGoals_NonBlockingWarning()
    {
        var note = AddNote(NoteType.Daily, "{}");

        var result = await _engine.RunClinicalValidationAsync(note.Id);

        var rule = result.FirstOrDefault(r => r.RuleId == "DOC_GOALS");
        Assert.NotNull(rule);
        Assert.False(rule.Blocking);
        Assert.Equal(ValidationSeverity.Warning, rule.Severity);
    }

    // ─── Documentation completeness: plan ─────────────────────────────────────

    [Fact]
    public async Task RunClinicalValidation_EvalWithNoPlan_BlockingError()
    {
        var note = AddNote(NoteType.Evaluation, "{}");

        var result = await _engine.RunClinicalValidationAsync(note.Id);

        var rule = result.FirstOrDefault(r => r.RuleId == "DOC_PLAN");
        Assert.NotNull(rule);
        Assert.True(rule.Blocking);
    }

    [Fact]
    public async Task RunClinicalValidation_EvalWithPlan_NoPlanRule()
    {
        var note = AddNote(NoteType.Evaluation,
            """{"plan": "Continue PT 3x/week for 4 weeks"}""");

        var result = await _engine.RunClinicalValidationAsync(note.Id);

        Assert.DoesNotContain(result, r => r.RuleId == "DOC_PLAN");
    }

    [Fact]
    public async Task RunClinicalValidation_DailyNote_PlanRuleDoesNotApply()
    {
        // Plan rule only applies to Eval and ProgressNote types
        var note = AddNote(NoteType.Daily, "{}");

        var result = await _engine.RunClinicalValidationAsync(note.Id);

        Assert.DoesNotContain(result, r => r.RuleId == "DOC_PLAN");
    }

    // ─── Compliance: certification period ─────────────────────────────────────

    [Fact]
    public async Task RunClinicalValidation_EvalMissingCertPeriod_BlockingError()
    {
        var note = AddNote(NoteType.Evaluation, "{}");

        var result = await _engine.RunClinicalValidationAsync(note.Id);

        var rule = result.FirstOrDefault(r => r.RuleId == "COMP_CERT");
        Assert.NotNull(rule);
        Assert.True(rule.Blocking);
        Assert.Equal(RuleCategory.Compliance, rule.Category);
    }

    [Fact]
    public async Task RunClinicalValidation_EvalWithCertPeriod_NoCertRule()
    {
        var note = AddNote(NoteType.Evaluation,
            """{"certificationPeriod": "2025-01-01 to 2025-03-31"}""");

        var result = await _engine.RunClinicalValidationAsync(note.Id);

        Assert.DoesNotContain(result, r => r.RuleId == "COMP_CERT");
    }

    [Fact]
    public async Task RunClinicalValidation_DailyNote_CertRuleDoesNotApply()
    {
        var note = AddNote(NoteType.Daily, "{}");

        var result = await _engine.RunClinicalValidationAsync(note.Id);

        Assert.DoesNotContain(result, r => r.RuleId == "COMP_CERT");
    }

    // ─── Compliance: functional limitation ────────────────────────────────────

    [Fact]
    public async Task RunClinicalValidation_EvalMissingFunctionalLimitation_BlockingError()
    {
        var note = AddNote(NoteType.Evaluation, "{}");

        var result = await _engine.RunClinicalValidationAsync(note.Id);

        var rule = result.FirstOrDefault(r => r.RuleId == "COMP_FUNCTIONAL");
        Assert.NotNull(rule);
        Assert.True(rule.Blocking);
    }

    [Fact]
    public async Task RunClinicalValidation_DailyMissingFunctionalLimitation_NonBlockingWarning()
    {
        var note = AddNote(NoteType.Daily, "{}");

        var result = await _engine.RunClinicalValidationAsync(note.Id);

        var rule = result.FirstOrDefault(r => r.RuleId == "COMP_FUNCTIONAL");
        Assert.NotNull(rule);
        Assert.False(rule.Blocking);
        Assert.Equal(ValidationSeverity.Warning, rule.Severity);
    }

    // ─── Compliance: CPT combinations ─────────────────────────────────────────

    [Fact]
    public async Task RunClinicalValidation_DualEvalCptCodes_BlockingError()
    {
        var note = AddNote(NoteType.Evaluation, "{}",
            cptCodesJson: """[{"code":"97161","units":1,"isTimed":false},{"code":"97162","units":1,"isTimed":false}]""");

        var result = await _engine.RunClinicalValidationAsync(note.Id);

        var rule = result.FirstOrDefault(r => r.RuleId == "COMP_CPT_DUAL_EVAL");
        Assert.NotNull(rule);
        Assert.True(rule.Blocking);
        Assert.Equal(ValidationSeverity.Error, rule.Severity);
        Assert.Contains("97161", rule.Message);
        Assert.Contains("97162", rule.Message);
    }

    [Fact]
    public async Task RunClinicalValidation_DuplicateCptCode_NonBlockingWarning()
    {
        var note = AddNote(NoteType.Daily, "{}",
            cptCodesJson: """[{"code":"97110","units":2,"isTimed":true},{"code":"97110","units":1,"isTimed":true}]""");

        var result = await _engine.RunClinicalValidationAsync(note.Id);

        var rule = result.FirstOrDefault(r => r.RuleId == "COMP_CPT_DUPLICATE");
        Assert.NotNull(rule);
        Assert.False(rule.Blocking);
        Assert.Equal(ValidationSeverity.Warning, rule.Severity);
        Assert.Contains("97110", rule.Message);
    }

    [Fact]
    public async Task RunClinicalValidation_NoCptCodes_NoCptRule()
    {
        var note = AddNote(NoteType.Daily, "{}");

        var result = await _engine.RunClinicalValidationAsync(note.Id);

        Assert.DoesNotContain(result, r => r.RuleId == "COMP_CPT_DUAL_EVAL");
        Assert.DoesNotContain(result, r => r.RuleId == "COMP_CPT_DUPLICATE");
    }

    // ─── Medicare: Plan of Care ────────────────────────────────────────────────

    [Fact]
    public async Task RunClinicalValidation_EvalMissingPlanOfCare_MedicarePocError()
    {
        var note = AddNote(NoteType.Evaluation, "{}");

        var result = await _engine.RunClinicalValidationAsync(note.Id);

        var rule = result.FirstOrDefault(r => r.RuleId == "MEDICARE_POC");
        Assert.NotNull(rule);
        Assert.True(rule.Blocking);
        Assert.Equal(RuleCategory.Medicare, rule.Category);
    }

    [Fact]
    public async Task RunClinicalValidation_EvalWithPlanOfCare_NoPocRule()
    {
        var note = AddNote(NoteType.Evaluation,
            """{"planOfCare": "Patient requires 6 weeks PT for lumbar pain"}""");

        var result = await _engine.RunClinicalValidationAsync(note.Id);

        Assert.DoesNotContain(result, r => r.RuleId == "MEDICARE_POC");
    }

    [Fact]
    public async Task RunClinicalValidation_DailyNote_PocRuleDoesNotApply()
    {
        var note = AddNote(NoteType.Daily, "{}");

        var result = await _engine.RunClinicalValidationAsync(note.Id);

        Assert.DoesNotContain(result, r => r.RuleId == "MEDICARE_POC");
    }

    // ─── Medicare: Functional Reporting ───────────────────────────────────────

    [Fact]
    public async Task RunClinicalValidation_ProgressNoteMissingFunctionalReport_NonBlockingWarning()
    {
        var note = AddNote(NoteType.ProgressNote, "{}");

        var result = await _engine.RunClinicalValidationAsync(note.Id);

        var rule = result.FirstOrDefault(r => r.RuleId == "MEDICARE_FUNCTIONAL_REPORT");
        Assert.NotNull(rule);
        Assert.False(rule.Blocking);
        Assert.Equal(ValidationSeverity.Warning, rule.Severity);
        Assert.Equal(RuleCategory.Medicare, rule.Category);
    }

    [Fact]
    public async Task RunClinicalValidation_ProgressNoteWithFunctionalReport_NoFunctionalReportRule()
    {
        var note = AddNote(NoteType.ProgressNote,
            """{"functionalLimitations": "Patient unable to perform ADLs independently"}""");

        var result = await _engine.RunClinicalValidationAsync(note.Id);

        Assert.DoesNotContain(result, r => r.RuleId == "MEDICARE_FUNCTIONAL_REPORT");
    }

    // ─── Pre-sign validation: blocking violations prevent signing ─────────────

    [Fact]
    public async Task RunClinicalValidation_NoteWithBlockingViolations_CannotSign()
    {
        // An Evaluation with empty content has multiple blocking violations.
        var note = AddNote(NoteType.Evaluation, "{}");

        var violations = await _engine.RunClinicalValidationAsync(note.Id);

        Assert.True(violations.Any(v => v.Blocking),
            "Expected at least one blocking violation for an empty Evaluation note.");
    }

    [Fact]
    public async Task RunClinicalValidation_DailyNoteWithValidContent_CanSign()
    {
        // A Daily note with functional limitations and at least one objective metric
        // should have no blocking violations.
        var note = AddNote(NoteType.Daily,
            """{"functionalLimitations": "Mild limitation in ambulation"}""");

        _context.ObjectiveMetrics.Add(new ObjectiveMetric
        {
            Id = Guid.NewGuid(),
            NoteId = note.Id,
            BodyPart = BodyPart.Lumbar,
            MetricType = MetricType.ROM,
            Value = "60 degrees"
        });
        await _context.SaveChangesAsync();

        var violations = await _engine.RunClinicalValidationAsync(note.Id);

        Assert.False(violations.Any(v => v.Blocking),
            "Expected no blocking violations for a valid Daily note.");
    }

    // ─── Medicare rule compliance: full Evaluation passes when complete ────────

    [Fact]
    public async Task RunClinicalValidation_CompleteEvaluation_NoBlockingViolations()
    {
        // Create a complete Evaluation note with all required fields.
        var note = AddNote(NoteType.Evaluation, """
            {
                "subjective": "Patient reports right shoulder pain rated 7/10 with overhead activity.",
                "goals": ["Short-term goal: walk 50ft"],
                "plan": "PT 3x/week for 6 weeks",
                "planOfCare": "Focus on gait training and strengthening",
                "certificationPeriod": "2025-01-01 to 2025-03-31",
                "functionalLimitations": "Unable to perform ADLs independently due to right hip pain"
            }
            """);

        _context.ObjectiveMetrics.Add(new ObjectiveMetric
        {
            Id = Guid.NewGuid(),
            NoteId = note.Id,
            BodyPart = BodyPart.Hip,
            MetricType = MetricType.ROM,
            Value = "90 degrees"
        });
        await _context.SaveChangesAsync();

        var violations = await _engine.RunClinicalValidationAsync(note.Id);

        var blocking = violations.Where(v => v.Blocking).ToList();
        Assert.Empty(blocking);
    }

    [Fact]
    public async Task RunClinicalValidation_V2EvaluationPayload_UsesNestedTypedFields()
    {
        var patientId = Guid.NewGuid();
        var payload = new NoteWorkspaceV2Payload
        {
            NoteType = NoteType.Evaluation,
            Subjective = new WorkspaceSubjectiveV2
            {
                Problems = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Pain" },
                FunctionalLimitations =
                [
                    new FunctionalLimitationEntryV2
                    {
                        BodyPart = BodyPart.Cervical,
                        Category = "Mobility",
                        Description = "Unable to turn head fully to look over shoulder while driving"
                    }
                ],
                NarrativeContext = new SubjectNarrativeContextV2
                {
                    ChiefComplaint = "Neck pain with driving"
                }
            },
            Assessment = new WorkspaceAssessmentV2
            {
                Goals =
                [
                    new WorkspaceGoalEntryV2
                    {
                        Description = "Patient will rotate head >=60 degrees to check over shoulder while seated within 3 weeks.",
                        Category = "Mobility",
                        Status = GoalStatus.Active
                    }
                ]
            },
            Plan = new WorkspacePlanV2
            {
                TreatmentFrequencyDaysPerWeek = [2],
                TreatmentDurationWeeks = [6],
                ComputedPlanOfCare = new ComputedPlanOfCareV2
                {
                    StartDate = new DateTime(2026, 3, 30),
                    EndDate = new DateTime(2026, 5, 10)
                }
            }
        };

        var note = new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = patientId,
            NoteType = NoteType.Evaluation,
            ContentJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            DateOfService = new DateTime(2026, 3, 30),
            LastModifiedUtc = DateTime.UtcNow
        };
        _context.ClinicalNotes.Add(note);
        _context.OutcomeMeasureResults.Add(new OutcomeMeasureResult
        {
            PatientId = patientId,
            NoteId = note.Id,
            MeasureType = OutcomeMeasureType.NeckDisabilityIndex,
            Score = 24,
            ClinicianId = Guid.NewGuid(),
            DateRecorded = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();

        var violations = await _engine.RunClinicalValidationAsync(note.Id);

        Assert.DoesNotContain(violations, violation => violation.RuleId == "SOAP_SUBJECTIVE");
        Assert.DoesNotContain(violations, violation => violation.RuleId == "DOC_GOALS");
        Assert.DoesNotContain(violations, violation => violation.RuleId == "DOC_PLAN");
        Assert.DoesNotContain(violations, violation => violation.RuleId == "COMP_CERT");
        Assert.DoesNotContain(violations, violation => violation.RuleId == "COMP_FUNCTIONAL");
        Assert.DoesNotContain(violations, violation => violation.RuleId == "DOC_OBJECTIVE");
    }

    [Fact]
    public async Task RunClinicalValidation_LegacyWorkspaceEvaluationPayload_ReusesCanonicalNormalization()
    {
        var note = AddNote(
            NoteType.Evaluation,
            """
            {
              "subjective": {
                "functionalLimitations": ["Unable to turn head fully while driving"],
                "currentPainScore": 6
              },
              "objective": {
                "clinicalObservationNotes": "Guarded cervical rotation"
              },
              "assessment": {
                "goals": [
                  {
                    "description": "Rotate head >= 60 degrees without pain"
                  }
                ]
              },
              "plan": {
                "selectedCptCodes": [
                  {
                    "code": "97110",
                    "description": "Therapeutic exercise",
                    "units": 2
                  }
                ],
                "treatmentFrequency": "2x/week",
                "treatmentDuration": "6 weeks"
              }
            }
            """);

        _context.ObjectiveMetrics.Add(new ObjectiveMetric
        {
            Id = Guid.NewGuid(),
            NoteId = note.Id,
            BodyPart = BodyPart.Cervical,
            MetricType = MetricType.ROM,
            Value = "55 degrees"
        });
        await _context.SaveChangesAsync();

        var violations = await _engine.RunClinicalValidationAsync(note.Id);

        Assert.DoesNotContain(violations, violation => violation.RuleId == "SOAP_SUBJECTIVE");
        Assert.DoesNotContain(violations, violation => violation.RuleId == "DOC_GOALS");
        Assert.DoesNotContain(violations, violation => violation.RuleId == "DOC_PLAN");
        Assert.DoesNotContain(violations, violation => violation.RuleId == "COMP_CERT");
        Assert.DoesNotContain(violations, violation => violation.RuleId == "COMP_FUNCTIONAL");
        Assert.DoesNotContain(violations, violation => violation.RuleId == "DOC_OBJECTIVE");
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private ClinicalNote AddNote(NoteType noteType, string contentJson,
        string cptCodesJson = "[]")
    {
        var note = new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = Guid.NewGuid(),
            NoteType = noteType,
            ContentJson = contentJson,
            CptCodesJson = cptCodesJson,
            DateOfService = DateTime.UtcNow,
            LastModifiedUtc = DateTime.UtcNow
        };
        _context.ClinicalNotes.Add(note);
        _context.SaveChanges();
        return note;
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }
}

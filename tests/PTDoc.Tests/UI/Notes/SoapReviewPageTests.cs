using Bunit;
using Microsoft.AspNetCore.Components;
using PTDoc.UI.Components.Notes;
using PTDoc.UI.Components.Notes.Models;
using Xunit;

namespace PTDoc.Tests.UI.Notes;

[Trait("Category", "CoreCi")]
public sealed class SoapReviewPageTests : TestContext
{
    [Fact]
    public void SoapReviewPage_RendersStructuredObjectiveAndPlanFields()
    {
        var note = new SoapNoteVm
        {
            PatientName = "Pat Example",
            PatientId = "PT-123",
            NoteType = "Progress Note",
            Subjective = new SubjectiveVm
            {
                StructuredFunctionalLimitations =
                [
                    new FunctionalLimitationEditorEntry
                    {
                        BodyPart = "Knee",
                        Category = "Mobility",
                        Description = "Difficulty squatting"
                    }
                ]
            },
            Objective = new ObjectiveVm
            {
                Metrics =
                [
                    new ObjectiveMetricRowEntry
                    {
                        Name = "Knee flexion 0-135",
                        MetricType = PTDoc.Core.Models.MetricType.ROM,
                        Value = "115",
                        NormValue = "135"
                    }
                ],
                SpecialTests =
                [
                    new SpecialTestEntry
                    {
                        Name = "McMurray",
                        Result = "Positive",
                        Notes = "Medial pain"
                    }
                ],
                TenderMuscles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Quadriceps" },
                PostureFindings = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Forward trunk lean" },
                ExerciseRows =
                [
                    new ExerciseRowEntry
                    {
                        SuggestedExercise = "Heel slides",
                        ActualExercisePerformed = "Heel slides"
                    }
                ]
            },
            Assessment = new AssessmentWorkspaceVm
            {
                AssessmentNarrative = "Progress assessment is visit-specific.",
                OverallPrognosis = "Excellent",
                DiagnosisCodes =
                [
                    new Icd10Entry { Code = "M25.561", Description = "Pain in right knee" }
                ]
            },
            Plan = new PlanVm
            {
                TreatmentFrequency = "2x/week",
                TreatmentDuration = "6 weeks",
                FollowUpInstructions = "Plan for next visit is progressive loading.",
                TreatmentFocuses = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Mobility" },
                GeneralInterventions =
                [
                    new GeneralInterventionEntry
                    {
                        Name = "Manual therapy",
                        Category = "Manual"
                    }
                ]
            }
        };

        var cut = RenderComponent<SoapReviewPage>(parameters => parameters
            .Add(component => component.Note, note)
            .Add(component => component.OnEditSection, EventCallback.Factory.Create<SoapSection>(this, _ => { }))
            .Add(component => component.OnBackToEdit, EventCallback.Factory.Create(this, () => { }))
            .Add(component => component.OnRegenerateSummary, EventCallback.Factory.Create(this, () => { }))
            .Add(component => component.OnRegenerateGoals, EventCallback.Factory.Create(this, () => { }))
            .Add(component => component.OnExportPdf, EventCallback.Factory.Create(this, () => { }))
            .Add(component => component.OnSubmit, EventCallback.Factory.Create(this, () => { })));

        Assert.Contains("Difficulty squatting", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Knee flexion 0-135", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("McMurray: Positive", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Tender muscles: Quadriceps", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Mobility", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Manual therapy (Manual)", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Progress assessment is visit-specific.", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Plan for next visit", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Prognosis", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("M25.561", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void SoapReviewPage_DischargeNote_RendersDischargeSpecificSubjectiveAndPlan()
    {
        var note = new SoapNoteVm
        {
            PatientName = "Pat Example",
            PatientId = "PT-123",
            NoteType = "Discharge Note",
            Subjective = new SubjectiveVm
            {
                CurrentPainScore = 1,
                BestPainScore = 0,
                WorstPainScore = 3,
                IsPainScoreDocumented = true
            },
            DischargeSubjective = new DischargeSubjectiveVm
            {
                GoalsMetStatus = "All functional goals met.",
                RemainingDifficulty = "Mild soreness after prolonged walking.",
                PercentImproved = 90,
                PatientReportedOutcome = "Patient is confident with independent HEP."
            },
            Plan = new PlanVm
            {
                PrimaryDischargeReason = "Reached goals",
                DischargeRecommendations = "Continue HEP three times weekly.",
                PostDischargeInstructions = "Return to PT if function declines.",
                FullDischargeSummary = "Discharged to independent self-management.",
                CompletedDischargeChecklistItems =
                [
                    "All goals reviewed and final status documented"
                ]
            }
        };

        var cut = RenderComponent<SoapReviewPage>(parameters => parameters
            .Add(component => component.Note, note)
            .Add(component => component.OnEditSection, EventCallback.Factory.Create<SoapSection>(this, _ => { }))
            .Add(component => component.OnBackToEdit, EventCallback.Factory.Create(this, () => { }))
            .Add(component => component.OnRegenerateSummary, EventCallback.Factory.Create(this, () => { }))
            .Add(component => component.OnRegenerateGoals, EventCallback.Factory.Create(this, () => { }))
            .Add(component => component.OnExportPdf, EventCallback.Factory.Create(this, () => { }))
            .Add(component => component.OnSubmit, EventCallback.Factory.Create(this, () => { })));

        Assert.Contains("Discharge Status", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("90% improved", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Mild soreness after prolonged walking.", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Discharge Reason", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Reached goals", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Continue HEP three times weekly.", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Discharged to independent self-management.", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Frequency", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Response to Treatment", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void SoapReviewPage_ReadOnlyHidesMutationControls()
    {
        var cut = RenderComponent<SoapReviewPage>(parameters => parameters
            .Add(component => component.Note, new SoapNoteVm { NoteType = "Progress Note" })
            .Add(component => component.IsReadOnly, true)
            .Add(component => component.CanSubmit, false)
            .Add(component => component.OnEditSection, EventCallback.Factory.Create<SoapSection>(this, _ => { }))
            .Add(component => component.OnBackToEdit, EventCallback.Factory.Create(this, () => { }))
            .Add(component => component.OnRegenerateSummary, EventCallback.Factory.Create(this, () => { }))
            .Add(component => component.OnRegenerateGoals, EventCallback.Factory.Create(this, () => { }))
            .Add(component => component.OnExportPdf, EventCallback.Factory.Create(this, () => { }))
            .Add(component => component.OnSubmit, EventCallback.Factory.Create(this, () => { })));

        var buttonLabels = cut.FindAll("button")
            .Select(button => button.TextContent.Trim())
            .ToArray();

        Assert.DoesNotContain("Edit", buttonLabels);
        Assert.DoesNotContain("Regenerate", buttonLabels);
        Assert.DoesNotContain("Back to Edit", buttonLabels);
        Assert.DoesNotContain("Submit & Lock Note", buttonLabels);
        Assert.Contains("Export PDF", buttonLabels);
    }
}

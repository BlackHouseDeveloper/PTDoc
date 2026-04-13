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
            Plan = new PlanVm
            {
                TreatmentFrequency = "2x/week",
                TreatmentDuration = "6 weeks",
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
    }
}

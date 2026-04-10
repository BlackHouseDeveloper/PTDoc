using Bunit;
using Microsoft.Extensions.DependencyInjection;
using PTDoc.Application.Intake;
using PTDoc.Application.ReferenceData;
using PTDoc.Infrastructure.ReferenceData;
using PTDoc.UI.Components.Intake.Cards;
using PTDoc.UI.Components.Intake.Models;
using PTDoc.UI.Components.Intake.Steps;
using Xunit;

namespace PTDoc.Tests.UI.Intake;

[Trait("Category", "CoreCi")]
public sealed class StructuredIntakeComponentsTests : TestContext
{
    public StructuredIntakeComponentsTests()
    {
        Services.AddSingleton<IIntakeReferenceDataCatalogService, IntakeReferenceDataCatalogService>();
    }

    [Fact]
    public void MedicalHistoryCard_RendersStructuredSelections()
    {
        var state = new IntakeWizardState
        {
            StructuredData = new IntakeStructuredDataDto
            {
                SchemaVersion = "2026-03-30",
                MedicationIds = ["zestril-lisinopril"]
            },
            SelectedComorbidities = ["Hypertension (High Blood Pressure)"],
            SelectedAssistiveDevices = ["Cane"],
            SelectedLivingSituations = ["Lives alone"],
            SelectedHouseLayoutOptions = ["Single-Story Home: Bedroom and bathroom on main floor"],
            HasCurrentMedications = true,
            HasOtherMedicalConditions = true,
            UsesAssistiveDevices = true
        };

        var cut = RenderComponent<MedicalHistoryCard>(parameters => parameters
            .Add(component => component.State, state));

        Assert.Contains("Zestril / Lisinopril", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Hypertension (High Blood Pressure)", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Lives alone", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Single-Story Home: Bedroom and bathroom on main floor", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PainDetailsStep_RendersStructuredBodyPartRecommendations()
    {
        var state = new IntakeWizardState
        {
            StructuredData = new IntakeStructuredDataDto
            {
                SchemaVersion = "2026-03-30",
                BodyPartSelections =
                [
                    new IntakeBodyPartSelectionDto
                    {
                        BodyPartId = "knee",
                        Lateralities = ["left"]
                    }
                ],
                PainDescriptorIds = ["aching"]
            }
        };

        var cut = RenderComponent<PainDetailsStep>(parameters => parameters
            .Add(component => component.State, state)
            .Add(component => component.ShowOutcomeMeasureRecommendations, true));

        Assert.Contains("Knee", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Aching", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LEFS", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("KOOS", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PainDetailsStep_StoresPainSeverityAndHidesOutcomeRecommendationsForNonClinicalViews()
    {
        var state = new IntakeWizardState
        {
            StructuredData = new IntakeStructuredDataDto
            {
                SchemaVersion = "2026-03-30",
                BodyPartSelections =
                [
                    new IntakeBodyPartSelectionDto
                    {
                        BodyPartId = "knee"
                    }
                ]
            }
        };

        var cut = RenderComponent<PainDetailsStep>(parameters => parameters
            .Add(component => component.State, state)
            .Add(component => component.ShowOutcomeMeasureRecommendations, false));

        cut.Find("input[type='range']").Input("7");

        Assert.Equal(7, state.PainSeverityScore);
        Assert.DoesNotContain("Auto-Selected Outcome Measures", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReviewStep_UsesPainSeverityScoreAndHidesOutcomeRecommendationsForNonClinicalViews()
    {
        var state = new IntakeWizardState
        {
            PainSeverityScore = 6,
            RecommendedOutcomeMeasures = ["LEFS", "KOOS"]
        };

        var cut = RenderComponent<ReviewStep>(parameters => parameters
            .Add(component => component.State, state)
            .Add(component => component.ShowOutcomeMeasureRecommendations, false));

        Assert.Contains("6/10", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Auto-Selected Outcome Measures", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }
}

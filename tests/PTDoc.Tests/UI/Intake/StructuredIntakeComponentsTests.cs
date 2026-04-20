using Bunit;
using Microsoft.Extensions.DependencyInjection;
using PTDoc.Application.Intake;
using PTDoc.Application.Outcomes;
using PTDoc.Application.ReferenceData;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Notes.Workspace;
using PTDoc.Infrastructure.Outcomes;
using PTDoc.Infrastructure.ReferenceData;
using PTDoc.UI.Components.Intake.Cards;
using PTDoc.UI.Components.Intake.Models;
using PTDoc.UI.Components.Intake.Steps;
using PTDoc.UI.Components.Notes.Workspace;
using Xunit;

namespace PTDoc.Tests.UI.Intake;

[Trait("Category", "CoreCi")]
public sealed class StructuredIntakeComponentsTests : TestContext
{
    public StructuredIntakeComponentsTests()
    {
        Services.AddSingleton<IIntakeReferenceDataCatalogService, IntakeReferenceDataCatalogService>();
        Services.AddSingleton<IIntakeBodyPartMapper>(new IntakeBodyPartMapper(new IntakeReferenceDataCatalogService()));
        Services.AddSingleton<IOutcomeMeasureRegistry, OutcomeMeasureRegistry>();
    }

    [Fact]
    public void MedicalHistoryCard_RendersStructuredSelections()
    {
        var state = new IntakeWizardState
        {
            StructuredData = new IntakeStructuredDataDto
            {
                SchemaVersion = "2026-03-30",
                MedicationIds = ["zestril-lisinopril"],
                ComorbidityIds = ["hypertension"],
                AssistiveDeviceIds = ["cane"],
                LivingSituationIds = ["lives-alone"],
                HouseLayoutOptionIds = ["single-story-main-floor-bed-bath"]
            },
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
    public void MedicalHistoryCard_CanonicalizesLegacySupplementalSelectionsIntoStructuredIds()
    {
        var state = new IntakeWizardState
        {
            SelectedComorbidities = ["Hypertension (High Blood Pressure)"],
            SelectedAssistiveDevices = ["Cane"],
            SelectedLivingSituations = ["Lives alone"],
            SelectedHouseLayoutOptions = ["Single-Story Home: Bedroom and bathroom on main floor"]
        };

        var cut = RenderComponent<MedicalHistoryCard>(parameters => parameters
            .Add(component => component.State, state));

        Assert.Equal(["hypertension"], state.StructuredData!.ComorbidityIds);
        Assert.Equal(["cane"], state.StructuredData.AssistiveDeviceIds);
        Assert.Equal(["lives-alone"], state.StructuredData.LivingSituationIds);
        Assert.Equal(["single-story-main-floor-bed-bath"], state.StructuredData.HouseLayoutOptionIds);
        Assert.Empty(state.SelectedComorbidities);
        Assert.Empty(state.SelectedAssistiveDevices);
        Assert.Empty(state.SelectedLivingSituations);
        Assert.Empty(state.SelectedHouseLayoutOptions);
        Assert.Contains("Hypertension (High Blood Pressure)", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Cane", cut.Markup, StringComparison.OrdinalIgnoreCase);
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
        Assert.Contains("PSFS", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NPRS", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("KOOS", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PainDetailsStep_UsesCanonicalUpperExtremityRecommendations()
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
                        BodyPartId = "shoulder",
                        Lateralities = ["right"]
                    }
                ]
            }
        };

        var cut = RenderComponent<PainDetailsStep>(parameters => parameters
            .Add(component => component.State, state)
            .Add(component => component.ShowOutcomeMeasureRecommendations, true));

        Assert.Contains("DASH", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("QuickDASH", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PSFS", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NPRS", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShoulderOutcomeSet_AlignsAcrossIntakeCatalogAndWorkspaceDropdown()
    {
        var registry = new OutcomeMeasureRegistry();
        var catalogs = new WorkspaceReferenceCatalogService(registry);
        var expected = registry
            .GetRecommendedMeasureAbbreviationsForBodyPart(BodyPart.Shoulder)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var state = new IntakeWizardState
        {
            StructuredData = new IntakeStructuredDataDto
            {
                SchemaVersion = "2026-03-30",
                BodyPartSelections =
                [
                    new IntakeBodyPartSelectionDto
                    {
                        BodyPartId = "shoulder",
                        Lateralities = ["right"]
                    }
                ]
            }
        };

        var intakeCut = RenderComponent<PainDetailsStep>(parameters => parameters
            .Add(component => component.State, state)
            .Add(component => component.ShowOutcomeMeasureRecommendations, true));

        var panelCut = RenderComponent<OutcomeMeasurePanel>(parameters => parameters
            .Add(component => component.PatientId, Guid.NewGuid())
            .Add(component => component.FilterBodyPart, BodyPart.Shoulder)
            .Add(component => component.SuggestedMeasures, expected));

        var catalogAbbreviations = catalogs
            .GetBodyRegionCatalog(BodyPart.Shoulder)
            .OutcomeMeasureOptions
            .Select(option => option.Split(" - ")[0])
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var dropdownAbbreviations = panelCut
            .FindAll("option")
            .Skip(1)
            .Select(option => option.TextContent.Split('—')[0].Trim())
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal(expected, catalogAbbreviations);
        Assert.Equal(expected, dropdownAbbreviations);
        Assert.Contains("QuickDASH", intakeCut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("QuickDASH", panelCut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("VAS", panelCut.Markup, StringComparison.OrdinalIgnoreCase);
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
            RecommendedOutcomeMeasures = ["LEFS", "PSFS", "NPRS"]
        };

        var cut = RenderComponent<ReviewStep>(parameters => parameters
            .Add(component => component.State, state)
            .Add(component => component.ShowOutcomeMeasureRecommendations, false));

        Assert.Contains("6/10", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Auto-Selected Outcome Measures", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReviewStep_UsesCanonicalConsentPacketForCompletionAndSupplementalSelections()
    {
        var state = new IntakeWizardState
        {
            TermsOfServiceAccepted = true,
            StructuredData = new IntakeStructuredDataDto
            {
                SchemaVersion = "2026-03-30",
                ComorbidityIds = ["hypertension"]
            },
            ConsentPacket = new IntakeConsentPacket
            {
                HipaaAcknowledged = true,
                TreatmentConsentAccepted = true,
                FinalAttestationAccepted = true,
                CommunicationCallConsent = true,
                CreditCardAuthorizationAccepted = true
            }
        };

        var cut = RenderComponent<ReviewStep>(parameters => parameters
            .Add(component => component.State, state));

        Assert.Contains("All required consents complete.", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Hypertension (High Blood Pressure)", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Authorized", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }
}

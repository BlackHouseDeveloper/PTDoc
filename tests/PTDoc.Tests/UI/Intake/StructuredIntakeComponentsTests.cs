using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using PTDoc.Application.Intake;
using PTDoc.Application.Outcomes;
using PTDoc.Application.ReferenceData;
using PTDoc.Application.Services;
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
    public void BasicInfoCard_UpdatesSexAtBirthAndAddressFields()
    {
        string? sexAtBirth = null;
        string? addressLine1 = null;
        string? city = null;
        string? stateOrProvince = null;
        string? postalCode = null;

        var cut = RenderComponent<BasicInfoCard>(parameters => parameters
            .Add(component => component.SexAtBirthChanged, EventCallback.Factory.Create<string?>(this, value => sexAtBirth = value))
            .Add(component => component.AddressLine1Changed, EventCallback.Factory.Create<string?>(this, value => addressLine1 = value))
            .Add(component => component.CityChanged, EventCallback.Factory.Create<string?>(this, value => city = value))
            .Add(component => component.StateOrProvinceChanged, EventCallback.Factory.Create<string?>(this, value => stateOrProvince = value))
            .Add(component => component.PostalCodeChanged, EventCallback.Factory.Create<string?>(this, value => postalCode = value)));

        cut.Find("#intake-sex-at-birth").Change("Female");
        cut.Find("#intake-address-line-1").Input("100 Beta Validation Way");
        cut.Find("#intake-city").Input("San Diego");
        cut.Find("#intake-state-or-province").Input("CA");
        cut.Find("#intake-postal-code").Input("92101");

        Assert.Equal("Female", sexAtBirth);
        Assert.Equal("100 Beta Validation Way", addressLine1);
        Assert.Equal("San Diego", city);
        Assert.Equal("CA", stateOrProvince);
        Assert.Equal("92101", postalCode);
    }

    [Fact]
    public void CareTeamCard_UpdatesDoctorFields()
    {
        string? primaryDoctorName = null;
        string? referringDoctorName = null;
        string? referringDoctorNpi = null;

        var cut = RenderComponent<CareTeamCard>(parameters => parameters
            .Add(component => component.PrimaryDoctorNameChanged, EventCallback.Factory.Create<string?>(this, value => primaryDoctorName = value))
            .Add(component => component.ReferringDoctorNameChanged, EventCallback.Factory.Create<string?>(this, value => referringDoctorName = value))
            .Add(component => component.ReferringDoctorNpiChanged, EventCallback.Factory.Create<string?>(this, value => referringDoctorNpi = value)));

        cut.Find("#intake-primary-doctor-name").Input("Dr. Primary");
        cut.Find("#intake-referring-doctor-name").Input("Dr. Referral");
        cut.Find("#intake-referring-doctor-npi").Input("1234567890");

        Assert.Equal("Dr. Primary", primaryDoctorName);
        Assert.Equal("Dr. Referral", referringDoctorName);
        Assert.Equal("1234567890", referringDoctorNpi);
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
    public void PainAssessmentStep2_UpdatesCurrentAndPriorFunctionalStatusFields()
    {
        var state = new IntakeWizardState
        {
            SelectedBodyRegions = [BodyRegion.ShoulderRightFront]
        };

        var cut = RenderComponent<PainAssessmentStep2>(parameters => parameters
            .Add(component => component.State, state));

        cut.Find("#intake-current-level-of-function").Input("Needs handrail for stairs and takes seated breaks every 10 minutes.");
        cut.Find("#intake-functional-limitations").Input("Difficulty reaching overhead cabinets.");

        Assert.Equal("Needs handrail for stairs and takes seated breaks every 10 minutes.", state.CurrentLevelOfFunction);
        Assert.Equal("Difficulty reaching overhead cabinets.", state.FunctionalLimitations);
        Assert.True(state.IsDirty);
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
        Assert.DoesNotContain("Auto-Selected Outcome Measures", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Continue to Outcome Measures", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Continue to Review", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LEFS", state.RecommendedOutcomeMeasures, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("PSFS", state.RecommendedOutcomeMeasures, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("NPRS", state.RecommendedOutcomeMeasures, StringComparer.OrdinalIgnoreCase);
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

        Assert.DoesNotContain("Auto-Selected Outcome Measures", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DASH", state.RecommendedOutcomeMeasures, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("QuickDASH", state.RecommendedOutcomeMeasures, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("PSFS", state.RecommendedOutcomeMeasures, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("NPRS", state.RecommendedOutcomeMeasures, StringComparer.OrdinalIgnoreCase);
        var assignment = Assert.Single(state.AssignedOutcomeMeasures);
        Assert.Equal("DASH", assignment.MeasureAbbreviation);
    }

    [Fact]
    public void PainDetailsStep_RecalculatesAssignedMeasureLaterality_WhenLateralityChanges()
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
                        BodyPartId = "shoulder"
                    }
                ]
            }
        };

        var cut = RenderComponent<PainDetailsStep>(parameters => parameters
            .Add(component => component.State, state));

        Assert.Null(Assert.Single(state.AssignedOutcomeMeasures).Laterality);

        cut.FindAll("button")
            .Single(button => string.Equals(button.TextContent.Trim(), "Left", StringComparison.Ordinal))
            .Click();

        Assert.Equal("left", Assert.Single(state.AssignedOutcomeMeasures).Laterality);
    }

    [Fact]
    public void OutcomeMeasuresStep_AllowsPatientEnteredInitialScoreAndSkip()
    {
        var state = new IntakeWizardState
        {
            AssignedOutcomeMeasures =
            [
                new AssignedOutcomeMeasureDraft
                {
                    BodyPartId = "knee",
                    BodyPartLabel = "Knee",
                    CanonicalBodyPart = "Knee",
                    MeasureAbbreviation = "LEFS",
                    MeasureFullName = "Lower Extremity Functional Scale",
                    IsPrimary = true
                }
            ]
        };

        var cut = RenderComponent<OutcomeMeasuresStep>(parameters => parameters
            .Add(component => component.State, state));

        Assert.Contains("Previous Outcome Measure or Functional Score", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("LEFS", cut.Markup, StringComparison.Ordinal);

        cut.Find("input[placeholder='Example: LEFS, DASH, ODI, knee score']").Input("LEFS");
        cut.Find("input[placeholder='Example: 42/80, 35%, 18 points']").Input("42/80");

        var report = Assert.Single(state.InitialOutcomeMeasureReports);
        Assert.Equal("LEFS", report.PatientEnteredMeasureName);
        Assert.Equal("42/80", report.ScoreText);
        Assert.False(report.Skipped);

        cut.Find("input[type='checkbox']").Change(true);
        Assert.True(Assert.Single(state.InitialOutcomeMeasureReports).Skipped);
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
        Assert.Contains("QuickDASH", state.RecommendedOutcomeMeasures, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Auto-Selected Outcome Measures", intakeCut.Markup, StringComparison.OrdinalIgnoreCase);
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
        Assert.True(state.PainSeverityProvided);
        Assert.DoesNotContain("Auto-Selected Outcome Measures", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReviewStep_UsesPainSeverityScoreAndHidesLegacyOutcomeRecommendations()
    {
        var state = new IntakeWizardState
        {
            PainSeverityScore = 6,
            PainSeverityProvided = true,
            RecommendedOutcomeMeasures = ["DASH", "QuickDASH", "PSFS", "NPRS", "LEFS"],
            AssignedOutcomeMeasures =
            [
                new AssignedOutcomeMeasureDraft
                {
                    BodyPartLabel = "Shoulder",
                    MeasureAbbreviation = "DASH",
                    MeasureFullName = "Disabilities of the Arm, Shoulder and Hand",
                    IsPrimary = true
                },
                new AssignedOutcomeMeasureDraft
                {
                    BodyPartLabel = "Knee",
                    MeasureAbbreviation = "LEFS",
                    MeasureFullName = "Lower Extremity Functional Scale",
                    IsPrimary = true
                }
            ]
        };

        var cut = RenderComponent<ReviewStep>(parameters => parameters
            .Add(component => component.State, state)
            .Add(component => component.ShowOutcomeMeasureRecommendations, true));

        Assert.Contains("6/10", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Outcome Measures", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Shoulder: DASH", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Knee: LEFS", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Auto-Selected Outcome Measures", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("QuickDASH", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PSFS", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("NPRS", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReviewStep_PatientMode_DoesNotRenderLegacyOutcomeRecommendations()
    {
        var state = new IntakeWizardState
        {
            IsPatientMode = true,
            RecommendedOutcomeMeasures = ["DASH", "QuickDASH", "PSFS", "NPRS"],
            AssignedOutcomeMeasures =
            [
                new AssignedOutcomeMeasureDraft
                {
                    BodyPartLabel = "Shoulder",
                    MeasureAbbreviation = "DASH",
                    IsPrimary = true
                }
            ]
        };

        var cut = RenderComponent<ReviewStep>(parameters => parameters
            .Add(component => component.State, state)
            .Add(component => component.ShowOutcomeMeasureRecommendations, true));

        Assert.Contains("Shoulder: DASH", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Auto-Selected Outcome Measures", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("QuickDASH", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PSFS", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("NPRS", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReviewStep_RendersDemographicsCareTeamInsuranceAndFunctionalLimitations()
    {
        var state = new IntakeWizardState
        {
            FullName = "Beta Patient",
            DateOfBirth = new DateTime(1990, 1, 1),
            SexAtBirth = "Female",
            EmailAddress = "beta.patient@example.com",
            PhoneNumber = "555-0300",
            AddressLine1 = "100 Beta Validation Way",
            City = "San Diego",
            StateOrProvince = "CA",
            PostalCode = "92101",
            PrimaryDoctorName = "Dr. Primary",
            ReferringDoctorName = "Dr. Referral",
            ReferringDoctorNpi = "1234567890",
            InsuranceCompanyName = "PFPT Beta PPO",
            MemberOrPolicyNumber = "BETA001",
            PayerType = "Commercial",
            InsuranceCoverageType = "Primary",
            FunctionalLimitations = "Difficulty walking longer than 10 minutes.",
            AssignedOutcomeMeasures =
            [
                new AssignedOutcomeMeasureDraft
                {
                    BodyPartLabel = "Knee",
                    MeasureAbbreviation = "LEFS",
                    MeasureFullName = "Lower Extremity Functional Scale",
                    IsPrimary = true
                }
            ],
            InitialOutcomeMeasureReports =
            [
                new InitialOutcomeMeasureReportDraft
                {
                    PatientEnteredMeasureName = "LEFS",
                    ScoreText = "42/80",
                    CompletedDate = new DateTime(2026, 5, 1)
                }
            ]
        };

        var cut = RenderComponent<ReviewStep>(parameters => parameters
            .Add(component => component.State, state));

        Assert.Contains("Female", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("100 Beta Validation Way", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Dr. Primary", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Dr. Referral", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("PFPT Beta PPO", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Difficulty walking longer than 10 minutes.", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Knee: LEFS", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("42/80", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void ReviewStep_ShowsEnteredInitialOutcomeReports_WhenSkippedAndEnteredReportsAreMixed()
    {
        var state = new IntakeWizardState
        {
            InitialOutcomeMeasureReports =
            [
                new InitialOutcomeMeasureReportDraft
                {
                    Skipped = true
                },
                new InitialOutcomeMeasureReportDraft
                {
                    PatientEnteredMeasureName = "LEFS",
                    ScoreText = "42/80"
                }
            ]
        };

        var cut = RenderComponent<ReviewStep>(parameters => parameters
            .Add(component => component.State, state));

        var priorScoreField = cut.FindAll(".review-step__field")
            .Single(field => field.TextContent.Contains("Previous Outcome Measure or Functional Score", StringComparison.Ordinal));

        Assert.Contains("42/80", priorScoreField.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Skipped", priorScoreField.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void ReviewStep_ReadOnlyReview_ShowsSubmitConfirmationNearSubmit()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var state = new IntakeWizardState
        {
            IsLocked = true,
            IsSubmitted = true,
            TermsOfServiceAccepted = true,
            ConsentPacket = new IntakeConsentPacket
            {
                HipaaAcknowledged = true,
                TreatmentConsentAccepted = true,
                FinalAttestationAccepted = true
            }
        };

        var cut = RenderComponent<ReviewStep>(parameters => parameters
            .Add(component => component.State, state)
            .Add(component => component.ValidationMessage, "Intake submitted successfully. This form is now locked."));

        var submitStatus = cut.Find("[data-testid='submit-status-message']");
        Assert.Contains("Intake submitted successfully", submitStatus.TextContent, StringComparison.Ordinal);
        Assert.Equal("intake-submit-status", submitStatus.Id);
    }

    [Fact]
    public void ReviewStep_ReadOnlyReview_NonSubmitMessagesStayInGeneralMessageRegion()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var state = new IntakeWizardState
        {
            IsLocked = true,
            IsSubmitted = true,
            TermsOfServiceAccepted = true,
            ConsentPacket = new IntakeConsentPacket
            {
                HipaaAcknowledged = true,
                TreatmentConsentAccepted = true,
                FinalAttestationAccepted = true
            }
        };

        var cut = RenderComponent<ReviewStep>(parameters => parameters
            .Add(component => component.State, state)
            .Add(component => component.ValidationMessage, "Intake record is unavailable for review."));

        Assert.Empty(cut.FindAll("[data-testid='submit-status-message']"));
        Assert.Contains("Intake record is unavailable for review.", cut.Markup, StringComparison.Ordinal);
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

    [Fact]
    public void ReviewStep_ReadOnlyReview_HidesMutationControlsAndCopy()
    {
        var state = new IntakeWizardState
        {
            IsLocked = true,
            IsSubmitted = true,
            TermsOfServiceAccepted = true,
            ConsentPacket = new IntakeConsentPacket
            {
                HipaaAcknowledged = true,
                TreatmentConsentAccepted = true,
                FinalAttestationAccepted = true,
                CommunicationCallConsent = true,
                CreditCardAuthorizationAccepted = true,
                AuthorizedContacts =
                [
                    new AuthorizedContact
                    {
                        Name = "Case Manager",
                        PhoneNumber = "555-0100",
                        Relationship = "Care coordinator"
                    }
                ]
            }
        };

        var cut = RenderComponent<ReviewStep>(parameters => parameters
            .Add(component => component.State, state));

        Assert.Contains("This intake has been submitted and locked. Review-only access is available.", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Intake Submitted", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Back to Previous Step", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("return to earlier steps", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("update the agreements", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("By clicking submit", cut.Markup, StringComparison.Ordinal);

        var submitButton = cut.Find("[data-testid='submit-button']");
        Assert.Equal("Intake Submitted", submitButton.TextContent.Trim());
        Assert.True(submitButton.HasAttribute("disabled"));
        Assert.All(cut.FindAll("button.review-step__edit-button"), button => Assert.True(button.HasAttribute("disabled")));
        Assert.All(cut.FindAll("input"), input => Assert.True(input.HasAttribute("disabled")));
    }

    [Fact]
    public void ReviewStep_ReadOnlyReview_ShowsClinicianMarkReviewedAction_WhenEligible()
    {
        var state = new IntakeWizardState
        {
            IntakeId = Guid.NewGuid(),
            IsLocked = true,
            IsSubmitted = true,
            TermsOfServiceAccepted = true,
            ConsentPacket = new IntakeConsentPacket
            {
                HipaaAcknowledged = true,
                TreatmentConsentAccepted = true,
                FinalAttestationAccepted = true
            }
        };
        var markedReviewed = false;

        var cut = RenderComponent<ReviewStep>(parameters => parameters
            .Add(component => component.State, state)
            .Add(component => component.CanMarkReviewed, true)
            .Add(component => component.ShowReviewedStatus, true)
            .Add(component => component.OnMarkReviewed, EventCallback.Factory.Create(this, () => markedReviewed = true)));

        Assert.Contains("Clinician Review Required", cut.Markup, StringComparison.Ordinal);
        var markReviewedButton = cut.FindAll("button")
            .Single(button => button.TextContent.Contains("Mark Reviewed", StringComparison.Ordinal));

        markReviewedButton.Click();

        Assert.True(markedReviewed);
    }
}

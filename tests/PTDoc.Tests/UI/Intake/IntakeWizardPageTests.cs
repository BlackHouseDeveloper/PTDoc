using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System.Net;
using PTDoc.Application.Configurations.Header;
using PTDoc.Application.DTOs;
using PTDoc.Application.Intake;
using PTDoc.Application.Outcomes;
using PTDoc.Application.ReferenceData;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Core.Services;
using PTDoc.Infrastructure.ReferenceData;
using PTDoc.Infrastructure.Outcomes;
using PTDoc.Infrastructure.Services;
using PTDoc.UI.Components.Intake.Models;
using PTDoc.UI.Pages.Intake;
using PTDoc.UI.Services;
using Xunit;

namespace PTDoc.Tests.UI.Intake;

[Trait("Category", "CoreCi")]
public sealed class IntakeWizardPageTests : TestContext
{
    public IntakeWizardPageTests()
    {
        Services.AddSingleton<IToastService, ToastService>();
        Services.AddSingleton<IIntakeReferenceDataCatalogService, IntakeReferenceDataCatalogService>();
        Services.AddSingleton<IIntakeBodyPartMapper>(new IntakeBodyPartMapper(new IntakeReferenceDataCatalogService()));
        Services.AddSingleton<IOutcomeMeasureRegistry, OutcomeMeasureRegistry>();
    }

    [Fact]
    public void ClinicianBlankIntake_RequiresPatientSelectionBeforeWizardRenders()
    {
        var authorization = this.AddTestAuthorization();
        authorization.SetAuthorized("pt-user");
        authorization.SetRoles(Roles.PT);

        var intakeService = new Mock<IIntakeService>(MockBehavior.Strict);
        var patientService = new Mock<IPatientService>(MockBehavior.Strict);
        patientService
            .Setup(service => service.SearchAsync(
                It.Is<string?>(query => string.IsNullOrWhiteSpace(query)),
                100,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PatientListItemResponse>());

        Services.AddLogging();
        Services.AddSingleton<IHeaderConfigurationService, HeaderConfigurationService>();
        Services.AddSingleton<IIntakeReferenceDataCatalogService, IntakeReferenceDataCatalogService>();
        Services.AddSingleton<IIntakeDemographicsValidationService, IntakeDemographicsValidationService>();
        Services.AddSingleton(intakeService.Object);
        Services.AddSingleton(patientService.Object);

        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Select a patient to start intake", cut.Markup, StringComparison.Ordinal);
            Assert.NotEmpty(cut.FindAll("[data-testid='clinician-patient-selector']"));
            Assert.Empty(cut.FindAll("[data-testid='demographics-step']"));
            Assert.Empty(cut.FindAll("[data-testid='continue-button']"));
        });

        intakeService.Verify(
            service => service.CreateTemporaryPatientAndDraftIntakeAsync(
                It.IsAny<IntakeResponseDraft>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        intakeService.Verify(
            service => service.EnsureDraftAsync(
                It.IsAny<Guid>(),
                It.IsAny<IntakeResponseDraft?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        intakeService.Verify(
            service => service.SaveDraftAsync(
                It.IsAny<IntakeResponseDraft>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void StandaloneBlankIntake_RequiresPatientSpecificInviteLinkBeforeWizardRenders()
    {
        var authorization = this.AddTestAuthorization();
        authorization.SetAuthorized("patient-user");
        authorization.SetRoles(Roles.Patient);

        var intakeService = new Mock<IIntakeService>(MockBehavior.Strict);

        Services.AddLogging();
        Services.AddSingleton<IHeaderConfigurationService, HeaderConfigurationService>();
        Services.AddSingleton<IIntakeReferenceDataCatalogService, IntakeReferenceDataCatalogService>();
        Services.AddSingleton<IIntakeDemographicsValidationService, IntakeDemographicsValidationService>();
        Services.AddSingleton(intakeService.Object);

        Services.GetRequiredService<NavigationManager>().NavigateTo("/intake?mode=patient");

        var cut = RenderPage();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Intake Link Required", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Use the secure intake link from your clinic", cut.Markup, StringComparison.Ordinal);
            Assert.Empty(cut.FindAll("[data-testid='demographics-step']"));
            Assert.Empty(cut.FindAll("[data-testid='continue-button']"));
        });

        intakeService.Verify(
            service => service.CreateTemporaryPatientAndDraftIntakeAsync(
                It.IsAny<IntakeResponseDraft>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void LegacyDraftCurrentStepThree_LoadsReviewStep()
    {
        var patientId = Guid.NewGuid();
        var authorization = this.AddTestAuthorization();
        authorization.SetAuthorized("pt-user");
        authorization.SetRoles(Roles.PT);

        var intakeService = CreateIntakeServiceMock(new IntakeResponseDraft
        {
            PatientId = patientId,
            CurrentStep = 3,
            AssignedOutcomeMeasures =
            [
                new AssignedOutcomeMeasureDraft
                {
                    BodyPartId = "knee",
                    MeasureAbbreviation = "LEFS"
                }
            ],
            FullName = "Legacy Review"
        });

        Services.GetRequiredService<NavigationManager>().NavigateTo($"/intake/{patientId}");

        var cut = RenderPage(patientId);

        cut.WaitForAssertion(() =>
        {
            Assert.NotEmpty(cut.FindAll("[data-testid='review-step']"));
            Assert.Empty(cut.FindAll("[data-testid='outcome-measures-step']"));
        });

        intakeService.Verify(
            service => service.GetLatestByPatientIdAsync(
                patientId,
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public void CurrentFlowDraftCurrentStepThree_LoadsOutcomeMeasuresStep()
    {
        var patientId = Guid.NewGuid();
        var authorization = this.AddTestAuthorization();
        authorization.SetAuthorized("pt-user");
        authorization.SetRoles(Roles.PT);

        CreateIntakeServiceMock(new IntakeResponseDraft
        {
            PatientId = patientId,
            IntakeFlowVersion = 2,
            CurrentStep = (int)IntakeStep.OutcomeMeasures,
            FullName = "Current Outcome"
        });

        Services.GetRequiredService<NavigationManager>().NavigateTo($"/intake/{patientId}");

        var cut = RenderPage(patientId);

        cut.WaitForAssertion(() =>
        {
            Assert.NotEmpty(cut.FindAll("[data-testid='outcome-measures-step']"));
            Assert.Empty(cut.FindAll("[data-testid='review-step']"));
        });
    }

    [Fact]
    public void SubmitSuccess_ShowsInlineMessageAndSuccessToast()
    {
        var patientId = Guid.NewGuid();
        var authorization = this.AddTestAuthorization();
        authorization.SetAuthorized("pt-user");
        authorization.SetRoles(Roles.PT);

        var intakeService = CreateIntakeServiceMock(CreateSubmittableReviewDraft(patientId));
        intakeService
            .Setup(service => service.SubmitAsync(
                It.IsAny<IntakeResponseDraft>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Services.GetRequiredService<NavigationManager>().NavigateTo($"/intake/{patientId}");

        var cut = RenderPage(patientId);

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='submit-button']")));
        cut.Find("[data-testid='submit-button']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Intake submitted successfully. This form is now locked.", cut.Markup, StringComparison.Ordinal);
            var toast = Assert.Single(Services.GetRequiredService<IToastService>().GetAll());
            Assert.Equal(ToastLevel.Success, toast.Level);
            Assert.Equal("Intake submitted successfully. This form is now locked.", toast.Message);
        });
    }

    [Fact]
    public void SubmitFailure_ShowsInlineMessageAndErrorToast()
    {
        var patientId = Guid.NewGuid();
        var authorization = this.AddTestAuthorization();
        authorization.SetAuthorized("pt-user");
        authorization.SetRoles(Roles.PT);

        var intakeService = CreateIntakeServiceMock(CreateSubmittableReviewDraft(patientId));
        intakeService
            .Setup(service => service.SubmitAsync(
                It.IsAny<IntakeResponseDraft>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Submission API rejected intake.", null, HttpStatusCode.BadRequest));

        Services.GetRequiredService<NavigationManager>().NavigateTo($"/intake/{patientId}");

        var cut = RenderPage(patientId);

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='submit-button']")));
        cut.Find("[data-testid='submit-button']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Submission API rejected intake.", cut.Markup, StringComparison.Ordinal);
            var toast = Assert.Single(Services.GetRequiredService<IToastService>().GetAll());
            Assert.Equal(ToastLevel.Error, toast.Level);
            Assert.Equal("Submission API rejected intake.", toast.Message);
        });
    }

    [Fact]
    public void StepDraftSaveFailure_ShowsErrorToast()
    {
        var patientId = Guid.NewGuid();
        var authorization = this.AddTestAuthorization();
        authorization.SetAuthorized("pt-user");
        authorization.SetRoles(Roles.PT);

        var intakeService = CreateIntakeServiceMock(new IntakeResponseDraft
        {
            PatientId = patientId,
            IntakeFlowVersion = 2,
            CurrentStep = (int)IntakeStep.PainAssessment,
            FullName = "Draft Save"
        });
        intakeService
            .Setup(service => service.SaveDraftAsync(
                It.IsAny<IntakeResponseDraft>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Draft API rejected intake.", null, HttpStatusCode.BadRequest));

        Services.GetRequiredService<NavigationManager>().NavigateTo($"/intake/{patientId}");

        var cut = RenderPage(patientId);

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='continue-button']")));
        cut.Find("[data-testid='continue-button']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Draft API rejected intake.", cut.Markup, StringComparison.Ordinal);
            var toast = Assert.Single(Services.GetRequiredService<IToastService>().GetAll());
            Assert.Equal(ToastLevel.Error, toast.Level);
            Assert.Equal("Draft API rejected intake.", toast.Message);
            Assert.True(GetWizardState(cut).IsDirty);
        });
    }

    private static IntakeResponseDraft CreateSubmittableReviewDraft(Guid patientId)
    {
        return new IntakeResponseDraft
        {
            PatientId = patientId,
            IntakeFlowVersion = 2,
            CurrentStep = (int)IntakeStep.Review,
            FullName = "Submit Ready",
            DateOfBirth = new DateTime(1988, 1, 1),
            TermsOfServiceAccepted = true,
            AccuracyConfirmed = true,
            HipaaAcknowledged = true,
            ConsentToTreatAcknowledged = true,
            ConsentPacket = new IntakeConsentPacket
            {
                HipaaAcknowledged = true,
                TreatmentConsentAccepted = true,
                FinalAttestationAccepted = true
            }
        };
    }

    private Mock<IIntakeService> CreateIntakeServiceMock(IntakeResponseDraft latestDraft)
    {
        var intakeService = new Mock<IIntakeService>(MockBehavior.Strict);
        intakeService
            .Setup(service => service.GetLatestByPatientIdAsync(
                latestDraft.PatientId!.Value,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(latestDraft);

        Services.AddLogging();
        Services.AddSingleton<IHeaderConfigurationService, HeaderConfigurationService>();
        Services.AddSingleton<IIntakeReferenceDataCatalogService, IntakeReferenceDataCatalogService>();
        Services.AddSingleton<IIntakeDemographicsValidationService, IntakeDemographicsValidationService>();
        Services.AddSingleton(intakeService.Object);

        return intakeService;
    }

    private IRenderedFragment RenderPage(Guid? patientId = null)
    {
        var authStateTask = Services
            .GetRequiredService<AuthenticationStateProvider>()
            .GetAuthenticationStateAsync();

        return Render(builder =>
        {
            builder.OpenComponent<CascadingValue<Task<AuthenticationState>>>(0);
            builder.AddAttribute(1, "Value", authStateTask);
            builder.AddAttribute(2, "ChildContent", (RenderFragment)(childBuilder =>
            {
                childBuilder.OpenComponent<IntakeWizardPage>(3);
                if (patientId.HasValue)
                {
                    childBuilder.AddAttribute(4, nameof(IntakeWizardPage.PatientId), patientId.Value);
                }

                childBuilder.CloseComponent();
            }));
            builder.CloseComponent();
        });
    }

    private static IntakeWizardState GetWizardState(IRenderedFragment cut)
    {
        var component = cut.FindComponent<IntakeWizardPage>();
        var stateProperty = typeof(IntakeWizardPage).GetProperty(
            "State",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        return Assert.IsType<IntakeWizardState>(stateProperty!.GetValue(component.Instance));
    }
}

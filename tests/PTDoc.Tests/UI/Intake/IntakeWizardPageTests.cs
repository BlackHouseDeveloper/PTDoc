using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System.Net;
using PTDoc.Application.Configurations.Header;
using PTDoc.Application.DTOs;
using PTDoc.Application.Intake;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Application.Outcomes;
using PTDoc.Application.ReferenceData;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Core.Services;
using PTDoc.Infrastructure.ReferenceData;
using PTDoc.Infrastructure.Outcomes;
using PTDoc.Infrastructure.Services;
using PTDoc.UI.Components;
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PendingSave_EncounterNavigationPreservesNewIdentityVersionAndDirtyState(bool failSave)
    {
        var patientId = Guid.NewGuid();
        var first = EncounterDraft(patientId);
        var second = EncounterDraft(patientId);
        var service = ConfigureEncounterDrafts(first, second);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var saves = new List<(Guid? IntakeId, Guid? NoteId, DateTime? Version)>();
        service.Setup(value => value.SaveDraftAsync(It.IsAny<IntakeResponseDraft>(), It.IsAny<CancellationToken>()))
            .Returns<IntakeResponseDraft, CancellationToken>(async (draft, _) =>
            {
                saves.Add((draft.IntakeId, draft.EncounterNoteId, draft.LastModifiedUtc));
                if (draft.EncounterNoteId == first.EncounterNoteId)
                {
                    await completion.Task;
                    if (failSave) throw new HttpRequestException("Delayed encounter A error");
                    draft.LastModifiedUtc = first.LastModifiedUtc!.Value.AddSeconds(1);
                }
            });
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo($"/intake/{patientId}?noteId={first.EncounterNoteId}");
        var cut = RenderPage(patientId);
        cut.WaitForAssertion(() => Assert.Equal(first.EncounterNoteId, GetWizardState(cut).EncounterNoteId));
        var saveTask = cut.Find("[data-testid='continue-button']").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Single(saves));

        await cut.FindComponent<IntakeWizardPage>().InvokeAsync(() =>
            navigation.NavigateTo($"/intake/{patientId}?noteId={second.EncounterNoteId}"));
        cut.WaitForAssertion(() => Assert.Equal(second.EncounterNoteId, GetWizardState(cut).EncounterNoteId));
        var newState = GetWizardState(cut);
        newState.IsDirty = true;
        completion.SetResult();
        await saveTask;

        Assert.Same(newState, GetWizardState(cut));
        Assert.Equal(second.IntakeId, newState.IntakeId);
        Assert.Equal(second.LastModifiedUtc, newState.LastModifiedUtc);
        Assert.True(newState.IsDirty);
        Assert.DoesNotContain("Delayed encounter A error", cut.Markup, StringComparison.Ordinal);
        await cut.Find("[data-testid='continue-button']").ClickAsync(new MouseEventArgs());
        Assert.Equal((second.IntakeId, second.EncounterNoteId, second.LastModifiedUtc), saves[1]);
    }

    [Fact]
    public async Task OpenInvite_EncounterNavigationGeneratesLinkForTheDisplayedEncounter()
    {
        var patientId = Guid.NewGuid();
        var first = EncounterDraft(patientId);
        var second = EncounterDraft(patientId);
        ConfigureEncounterDrafts(first, second);
        var delivery = new Mock<IIntakeDeliveryService>(MockBehavior.Strict);
        delivery.Setup(value => value.GetDeliveryStatusAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => new IntakeDeliveryStatusResponse { IntakeId = id, PatientId = patientId });
        delivery.Setup(value => value.GetDeliveryBundleAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => new IntakeDeliveryBundleResponse
            {
                IntakeId = id,
                PatientId = patientId,
                InviteUrl = $"https://example.test/intake/{id}",
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(1)
            });
        Services.AddSingleton(delivery.Object);
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo($"/intake/{patientId}?noteId={first.EncounterNoteId}");
        var cut = RenderPage(patientId);
        cut.FindAll("button").Single(button => button.TextContent == "Send intake link").Click();
        var firstModal = cut.FindComponent<SendIntakeModal>().Instance;
        cut.FindAll("button").Single(button => button.TextContent.Contains("Generate Link", StringComparison.Ordinal)).Click();
        cut.WaitForAssertion(() => Assert.Contains(first.IntakeId!.Value.ToString(), cut.Find("#generated-link").GetAttribute("value")));

        await cut.FindComponent<IntakeWizardPage>().InvokeAsync(() =>
            navigation.NavigateTo($"/intake/{patientId}?noteId={second.EncounterNoteId}"));
        cut.WaitForAssertion(() => Assert.Equal(second.EncounterNoteId, cut.FindComponent<SendIntakeModal>().Instance.EncounterNoteId));
        Assert.NotSame(firstModal, cut.FindComponent<SendIntakeModal>().Instance);
        Assert.Empty(cut.FindAll("#generated-link"));
        cut.FindAll("button").Single(button => button.TextContent.Contains("Generate Link", StringComparison.Ordinal)).Click();
        cut.WaitForAssertion(() => Assert.Contains(second.IntakeId!.Value.ToString(), cut.Find("#generated-link").GetAttribute("value")));
        delivery.Verify(value => value.GetDeliveryBundleAsync(second.IntakeId!.Value, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public async Task PendingSubmit_EncounterNavigationDiscardsCompletion(int failure, bool delayNextLoad)
    {
        var patientId = Guid.NewGuid();
        var first = SubmittableEncounterDraft(patientId);
        var second = SubmittableEncounterDraft(patientId);
        var service = ConfigureEncounterDrafts(first, second);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var nextLoad = new TaskCompletionSource<IntakeResponseDraft?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var submissions = new List<(Guid? Id, DateTime? Version)>();
        service.Setup(value => value.SubmitAsync(It.IsAny<IntakeResponseDraft>(), It.IsAny<CancellationToken>()))
            .Returns<IntakeResponseDraft, CancellationToken>(async (draft, _) =>
            {
                submissions.Add((draft.IntakeId, draft.LastModifiedUtc));
                if (draft.IntakeId != first.IntakeId) return;
                await completion.Task;
                // SubmitAsync can advance the version during its preliminary save,
                // even when the final submit request fails.
                draft.LastModifiedUtc = first.LastModifiedUtc!.Value.AddMinutes(1);
                if (failure == 1) throw new HttpRequestException("Obsolete submit failure");
                if (failure == 2) throw new InvalidOperationException("Obsolete submit failure");
            });
        if (delayNextLoad)
            service.Setup(value => value.GetForNoteAsync(patientId, second.EncounterNoteId!.Value, It.IsAny<CancellationToken>()))
                .Returns(nextLoad.Task);
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo($"/intake/{patientId}?noteId={first.EncounterNoteId}");
        var cut = RenderPage(patientId);
        var original = GetWizardState(cut);
        var submit = cut.Find("[data-testid='submit-button']").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Single(submissions));
        await cut.FindComponent<IntakeWizardPage>().InvokeAsync(() =>
            navigation.NavigateTo($"/intake/{patientId}?noteId={second.EncounterNoteId}"));
        cut.WaitForAssertion(() => service.Verify(value => value.GetForNoteAsync(patientId, second.EncounterNoteId!.Value, It.IsAny<CancellationToken>()), Times.Once));
        if (!delayNextLoad)
        {
            cut.WaitForAssertion(() => Assert.Equal(second.IntakeId, GetWizardState(cut).IntakeId));
            GetWizardState(cut).IsDirty = true;
        }
        completion.SetResult();
        await submit;
        if (delayNextLoad)
        {
            Assert.Same(original, GetWizardState(cut));
            Assert.False(original.IsSubmitted);
            Assert.Equal(first.LastModifiedUtc, original.LastModifiedUtc);
            nextLoad.SetResult(second);
            cut.WaitForAssertion(() => Assert.Equal(second.IntakeId, GetWizardState(cut).IntakeId));
            GetWizardState(cut).IsDirty = true;
        }
        var current = GetWizardState(cut);
        Assert.Equal(second.IntakeId, current.IntakeId);
        Assert.Equal(second.LastModifiedUtc, current.LastModifiedUtc);
        Assert.False(current.IsLocked);
        Assert.False(current.IsSubmitted);
        Assert.False(current.IsSubmitting);
        Assert.True(current.IsDirty);
        Assert.Empty(Services.GetRequiredService<IToastService>().GetAll());
        Assert.DoesNotContain("Obsolete submit failure", cut.Markup, StringComparison.Ordinal);
        await cut.Find("[data-testid='submit-button']").ClickAsync(new MouseEventArgs());
        Assert.Equal((second.IntakeId, second.LastModifiedUtc), submissions[1]);
        Assert.True(current.IsSubmitted);
    }

    [Theory]
    [InlineData("accept-save", false)]
    [InlineData("accept-save", true)]
    [InlineData("accept-reload", false)]
    [InlineData("accept-reload", true)]
    [InlineData("retry-reload", false)]
    [InlineData("retry-reload", true)]
    [InlineData("refresh", false)]
    [InlineData("refresh", true)]
    public async Task PendingProfileOperation_EncounterNavigationPreservesDisplayedDraft(string phase, bool fail)
    {
        var patientId = Guid.NewGuid();
        var first = EncounterDraft(patientId);
        first.ProfilePrefill = new IntakeProfilePrefill { PhoneNumber = "555-0101", Fingerprint = "offer-a" };
        var second = EncounterDraft(patientId);
        second.FullName = "Encounter B answers";
        second.ProfilePrefill = new IntakeProfilePrefill { PhoneNumber = "555-0102", Fingerprint = "offer-b" };
        var service = ConfigureEncounterDrafts(first, second);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiting = false;
        var firstReads = 0;
        service.Setup(value => value.GetForNoteAsync(patientId, first.EncounterNoteId!.Value, It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                firstReads++;
                if (firstReads == 1) return first;
                if (phase == "retry-reload" && firstReads == 2) throw new HttpRequestException("Retry the saved intake");
                waiting = true;
                await completion.Task;
                if (fail) throw new HttpRequestException("Obsolete profile failure");
                return first;
            });
        var savedIds = new List<Guid?>();
        service.Setup(value => value.SaveDraftAsync(It.IsAny<IntakeResponseDraft>(), It.IsAny<CancellationToken>()))
            .Returns<IntakeResponseDraft, CancellationToken>(async (draft, _) =>
            {
                savedIds.Add(draft.IntakeId);
                if (phase != "accept-save" || draft.IntakeId != first.IntakeId) return;
                waiting = true;
                await completion.Task;
                if (fail) throw new HttpRequestException("Obsolete profile failure");
            });
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo($"/intake/{patientId}?noteId={first.EncounterNoteId}");
        var cut = RenderPage(patientId);
        if (phase == "retry-reload")
        {
            await cut.Find("[data-testid='intake-apply-profile']").ClickAsync(new MouseEventArgs());
            cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='intake-reload-saved']")));
        }
        var selector = phase == "refresh" ? "intake-refresh-profile"
            : phase == "retry-reload" ? "intake-reload-saved" : "intake-apply-profile";
        var operation = cut.Find($"[data-testid='{selector}']").ClickAsync(new MouseEventArgs());
        cut.WaitForAssertion(() => Assert.True(waiting));
        await cut.FindComponent<IntakeWizardPage>().InvokeAsync(() =>
            navigation.NavigateTo($"/intake/{patientId}?noteId={second.EncounterNoteId}"));
        cut.WaitForAssertion(() => Assert.Equal(second.IntakeId, GetWizardState(cut).IntakeId));
        var current = GetWizardState(cut);
        current.IsDirty = true;
        completion.SetResult();
        await operation;
        Assert.Same(current, GetWizardState(cut));
        Assert.Equal(second.IntakeId, current.IntakeId);
        Assert.Equal(second.LastModifiedUtc, current.LastModifiedUtc);
        Assert.Equal(second.FullName, current.FullName);
        Assert.Equal("offer-b", current.ProfilePrefill!.Fingerprint);
        Assert.True(current.IsDirty);
        Assert.Empty(cut.FindAll("[data-testid='intake-profile-reload']"));
        Assert.DoesNotContain("Obsolete profile failure", cut.Markup, StringComparison.Ordinal);
        if (phase == "accept-save") Assert.Equal(1, firstReads);
        service.Verify(value => value.GetForNoteAsync(patientId, second.EncounterNoteId!.Value, It.IsAny<CancellationToken>()), Times.Once);
        await cut.Find("[data-testid='continue-button']").ClickAsync(new MouseEventArgs());
        Assert.Equal(second.IntakeId, savedIds.Last());
    }

    private static IntakeResponseDraft SubmittableEncounterDraft(Guid patientId)
    {
        var draft = CreateSubmittableReviewDraft(patientId);
        draft.IntakeId = Guid.NewGuid();
        draft.EncounterNoteId = Guid.NewGuid();
        draft.QuestionnaireDefinitionId = IntakeQuestionnaireDefinitions.EvaluationFullV1;
        draft.LastModifiedUtc = DateTime.UtcNow;
        return draft;
    }

    [Theory]
    [InlineData(NoteType.Daily, "daily-subjective-section")]
    [InlineData(NoteType.ProgressNote, "progress-subjective-section")]
    [InlineData(NoteType.Discharge, "discharge-subjective-section")]
    public async Task ShortVisit_ShowsCorrespondingSubjectiveAndReviewWithoutHistoryModules(NoteType type, string selector)
    {
        var patientId = Guid.NewGuid();
        var draft = EncounterDraft(patientId);
        draft.QuestionnaireDefinitionId = IntakeQuestionnaireDefinitions.ForNoteType(type);
        draft.CurrentStep = (int)IntakeStep.Demographics;
        draft.PhiReleaseAuthorized = false;
        draft.VisitQuestionnaire = new()
        {
            CurrentPain = 0,
            Daily = type == NoteType.Daily ? new() : null,
            Progress = type == NoteType.ProgressNote ? new() { Activities = new() } : null,
            Discharge = type == NoteType.Discharge ? new() : null
        };
        var service = ConfigureEncounterDrafts(draft);
        service.Setup(value => value.SaveDraftAsync(It.IsAny<IntakeResponseDraft>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        service.Setup(value => value.SubmitAsync(It.IsAny<IntakeResponseDraft>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var workspace = new Mock<INoteWorkspaceService>(MockBehavior.Strict);
        Services.AddSingleton(workspace.Object);
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/intake/{patientId}?noteId={draft.EncounterNoteId}");
        var cut = RenderPage(patientId);
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll($"[data-testid='{selector}']")));
        Assert.Contains("Step 1 of 2", cut.Markup);
        Assert.Empty(cut.FindAll("[data-testid='intake-insurance-reconciliation']"));
        Assert.Empty(cut.FindAll("#discharge-reason"));
        Assert.DoesNotContain("Medical History / Pain Assessment", cut.Markup);
        await cut.FindAll("button").Single(button => button.TextContent == "Review answers").ClickAsync(new());
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid='review-step']")));
        Assert.Contains("Review your visit answers", cut.Markup);
        Assert.Contains("Step 2 of 2", cut.Markup);
        Assert.DoesNotContain("<h2>Medical History</h2>", cut.Markup);
        service.Verify(value => value.SaveDraftAsync(It.Is<IntakeResponseDraft>(saved =>
            saved.VisitQuestionnaire != null && saved.VisitQuestionnaire.CurrentPain == 0 && saved.IntakeId == draft.IntakeId),
            It.IsAny<CancellationToken>()), Times.Once);
        cut.Find("#terms-privacy-ack").Change(true);
        foreach (var label in new[] { "I acknowledge receipt of the HIPAA", "I have reviewed my answers" })
            cut.FindAll("label").Single(element => element.TextContent.Contains(label, StringComparison.Ordinal))
                .QuerySelector("input")!.Change(true);
        Assert.True(cut.FindAll("button").Single(button => button.TextContent.Trim() == "Submit Intake Form").HasAttribute("disabled"));
        cut.Find("[data-testid='intake-treatment-consent']").Change(true);
        var submit = cut.FindAll("button").Single(button => button.TextContent.Trim() == "Submit Intake Form");
        Assert.False(submit.HasAttribute("disabled"));
        await submit.ClickAsync(new());
        service.Verify(value => value.SubmitAsync(It.Is<IntakeResponseDraft>(saved =>
            saved.ConsentPacket != null && saved.ConsentPacket.TreatmentConsentAccepted == true && saved.HipaaAcknowledged),
            It.IsAny<CancellationToken>()), Times.Once);
        workspace.VerifyNoOtherCalls(); // Patient questionnaires use their issued activity catalog, not staff APIs.
    }

    [Theory]
    [InlineData(NoteType.Daily)]
    [InlineData(NoteType.ProgressNote)]
    [InlineData(NoteType.Discharge)]
    public async Task ShortVisit_PartialSaveAllowsMissingPain_AndReviewShowsValidation(NoteType type)
    {
        var draft = EncounterDraft(Guid.NewGuid());
        draft.QuestionnaireDefinitionId = IntakeQuestionnaireDefinitions.ForNoteType(type);
        draft.CurrentStep = (int)IntakeStep.Demographics;
        draft.VisitQuestionnaire = new()
        {
            Daily = type == NoteType.Daily ? new() : null,
            Progress = type == NoteType.ProgressNote ? new() { Activities = new() } : null,
            Discharge = type == NoteType.Discharge ? new() : null
        };
        var service = ConfigureEncounterDrafts(draft);
        service.Setup(value => value.SaveDraftAsync(It.IsAny<IntakeResponseDraft>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        Services.AddSingleton(new Mock<INoteWorkspaceService>(MockBehavior.Strict).Object);
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/intake/{draft.PatientId}?noteId={draft.EncounterNoteId}");
        var cut = RenderPage(draft.PatientId);
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid='intake-save-partial']")));
        await cut.Find("[data-testid='intake-save-partial']").ClickAsync(new());
        service.Verify(value => value.SaveDraftAsync(It.Is<IntakeResponseDraft>(saved =>
            saved.VisitQuestionnaire != null && saved.VisitQuestionnaire.CurrentPain == null), It.IsAny<CancellationToken>()), Times.Once);
        await cut.FindAll("button").Single(button => button.TextContent == "Review answers").ClickAsync(new());
        Assert.NotEmpty(cut.Find("[data-testid='intake-page-feedback']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='review-step']"));
    }

    [Theory]
    [InlineData(NoteType.Daily, "daily-additional-information")]
    [InlineData(NoteType.ProgressNote, "progress-additional-information")]
    [InlineData(NoteType.Discharge, "discharge-goals-met")]
    public async Task ShortVisit_TypingRetainsFormModelAndSavesCompleteText(NoteType type, string fieldId)
    {
        var draft = EncounterDraft(Guid.NewGuid());
        draft.QuestionnaireDefinitionId = IntakeQuestionnaireDefinitions.ForNoteType(type);
        draft.CurrentStep = (int)IntakeStep.Demographics;
        draft.VisitQuestionnaire = new()
        {
            Daily = type == NoteType.Daily ? new() : null,
            Progress = type == NoteType.ProgressNote ? new() { Activities = new() } : null,
            Discharge = type == NoteType.Discharge ? new() : null
        };
        var service = ConfigureEncounterDrafts(draft);
        IntakeResponseDraft? saved = null;
        service.Setup(value => value.SaveDraftAsync(It.IsAny<IntakeResponseDraft>(), It.IsAny<CancellationToken>()))
            .Callback<IntakeResponseDraft, CancellationToken>((value, _) => saved = value)
            .Returns(Task.CompletedTask);
        Services.AddSingleton(new Mock<INoteWorkspaceService>(MockBehavior.Strict).Object);
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/intake/{draft.PatientId}?noteId={draft.EncounterNoteId}");
        var cut = RenderPage(draft.PatientId);
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll($"#{fieldId}")));
        var form = cut.FindComponent<Microsoft.AspNetCore.Components.Forms.EditForm>();
        var model = form.Instance.Model;
        foreach (var text in new[] { "I", "I ", "I can walk" })
        {
            cut.Find($"#{fieldId}").Input(text);
            // A new EditForm model replaces its control subtree, dropping live browser focus.
            Assert.Same(model, form.Instance.Model);
        }
        await cut.Find("[data-testid='intake-save-partial']").ClickAsync(new());
        Assert.NotNull(saved?.VisitQuestionnaire);
        var actual = type switch
        {
            NoteType.Daily => saved.VisitQuestionnaire.Daily!.AdditionalInformation,
            NoteType.ProgressNote => saved.VisitQuestionnaire.Progress!.AdditionalInformation,
            _ => saved.VisitQuestionnaire.Discharge!.GoalsMetStatus
        };
        Assert.Equal("I can walk", actual);
    }

    private static IntakeResponseDraft EncounterDraft(Guid patientId) => new()
    {
        PatientId = patientId,
        IntakeId = Guid.NewGuid(),
        EncounterNoteId = Guid.NewGuid(),
        QuestionnaireDefinitionId = IntakeQuestionnaireDefinitions.EvaluationFullV1,
        LastModifiedUtc = DateTime.UtcNow,
        IntakeFlowVersion = 2,
        CurrentStep = (int)IntakeStep.PainAssessment,
        FullName = "Synthetic Encounter"
    };

    private Mock<IIntakeService> ConfigureEncounterDrafts(params IntakeResponseDraft[] drafts)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var authorization = this.AddTestAuthorization();
        authorization.SetAuthorized("synthetic-pt");
        authorization.SetRoles(Roles.PT);
        Services.AddSingleton(Mock.Of<IIntakeDeliveryService>());
        var service = CreateIntakeServiceMock(drafts[0]);
        service.Setup(value => value.GetForNoteAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid patientId, Guid noteId, CancellationToken _) => drafts.Single(draft => draft.PatientId == patientId && draft.EncounterNoteId == noteId));
        service.Setup(value => value.EnsureDraftAsync(It.IsAny<Guid>(), It.IsAny<IntakeResponseDraft?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid patientId, IntakeResponseDraft? seed, CancellationToken _) =>
                IntakeEnsureDraftResult.Existing(drafts.Single(draft => draft.PatientId == patientId && draft.EncounterNoteId == seed!.EncounterNoteId)));
        return service;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProfilePrefill_RequiresExplicitChoice_AndRetainsExistingAnswers(bool apply)
    {
        var patientId = Guid.NewGuid();
        var authorization = this.AddTestAuthorization();
        authorization.SetAuthorized("synthetic-pt");
        authorization.SetRoles(Roles.PT);
        Services.AddSingleton(new Mock<PTDoc.Application.Providers.IProviderDirectoryService>().Object);
        var initial = new IntakeResponseDraft
        {
            PatientId = patientId,
            FullName = "Patient supplied name",
            PhoneNumber = "",
            ProfilePrefill = new IntakeProfilePrefill { FullName = "Saved name", PhoneNumber = "555-0107", Fingerprint = "offer" }
        };
        var service = CreateIntakeServiceMock(initial);
        IntakeResponseDraft? accepted = null;
        service.Setup(value => value.SaveDraftAsync(It.IsAny<IntakeResponseDraft>(), It.IsAny<CancellationToken>()))
            .Callback<IntakeResponseDraft, CancellationToken>((draft, _) =>
            {
                Assert.Equal(IntakeProfileAction.FillBlankFields, draft.ProfileUpdate!.Action);
                Assert.Equal("offer", draft.ProfileUpdate.ExpectedFingerprint);
                initial.ProfilePrefill!.ApplyTo(draft, confirmedBlankFields: true);
                draft.ProfileUpdate = null;
                draft.ProfilePrefill = null;
                accepted = draft;
            }).Returns(Task.CompletedTask);
        service.Setup(value => value.GetLatestByPatientIdAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => accepted ?? initial);
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/intake/{patientId}");
        var cut = RenderPage(patientId);
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='intake-profile-prefill']")));
        Assert.Equal("", GetWizardState(cut).PhoneNumber);
        cut.Find(apply ? "[data-testid='intake-apply-profile']" : "[data-testid='intake-dismiss-profile']").Click();
        Assert.Equal(apply ? "555-0107" : "", GetWizardState(cut).PhoneNumber);
        Assert.Equal("Patient supplied name", GetWizardState(cut).FullName);
        Assert.Empty(cut.FindAll("[data-testid='intake-profile-prefill']"));
    }

    [Fact]
    public void ProfilePrefill_SelfPayOnlyOfferSurvivesWizardSaveWithIndependentPolicyVersions()
    {
        var patientId = Guid.NewGuid();
        var primaryId = Guid.NewGuid();
        var secondaryId = Guid.NewGuid();
        var primaryVersion = DateTime.UtcNow;
        var secondaryVersion = primaryVersion.AddDays(-1);
        var authorization = this.AddTestAuthorization();
        authorization.SetAuthorized("synthetic-pt");
        authorization.SetRoles(Roles.PT);
        var initial = new IntakeResponseDraft
        {
            PatientId = patientId,
            IntakeFlowVersion = 2,
            CurrentStep = (int)IntakeStep.PainAssessment,
            FullName = "Mary Jane Smith",
            SecondaryInsuranceCompanyName = "Existing secondary",
            PrefilledSecondaryPolicyId = secondaryId,
            PrefilledSecondaryPolicyModifiedUtc = secondaryVersion,
            ProfilePrefillSourceModifiedUtc = secondaryVersion,
            ProfilePrefill = new IntakeProfilePrefill
            {
                Fingerprint = "self-pay-offer",
                PayerType = "Self Pay",
                PrimaryPolicyId = primaryId,
                PrimaryPolicyModifiedUtc = primaryVersion,
                SourceModifiedUtc = primaryVersion
            }
        };
        var service = CreateIntakeServiceMock(initial);
        IntakeResponseDraft? saved = null;
        service.Setup(value => value.SaveDraftAsync(It.IsAny<IntakeResponseDraft>(), It.IsAny<CancellationToken>()))
            .Callback<IntakeResponseDraft, CancellationToken>((draft, _) =>
            {
                if (draft.ProfileUpdate is not null)
                {
                    Assert.Equal("self-pay-offer", draft.ProfileUpdate.ExpectedFingerprint);
                    initial.ProfilePrefill!.ApplyTo(draft, confirmedBlankFields: true);
                    draft.ProfileUpdate = null;
                    draft.ProfilePrefill = null;
                }
                saved = draft;
            })
            .Returns(Task.CompletedTask);
        service.Setup(value => value.GetLatestByPatientIdAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => saved ?? initial);
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/intake/{patientId}");
        var cut = RenderPage(patientId);

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='intake-profile-prefill']")));
        cut.Find("[data-testid='intake-apply-profile']").Click();
        Assert.Equal("Self Pay", GetWizardState(cut).PayerType);
        cut.Find("[data-testid='continue-button']").Click();
        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(saved);
            Assert.Equal("Self Pay", saved.PayerType);
            Assert.Equal(primaryId, saved.PrefilledPrimaryPolicyId);
            Assert.Equal(primaryVersion, saved.PrefilledPrimaryPolicyModifiedUtc);
            Assert.Equal(secondaryId, saved.PrefilledSecondaryPolicyId);
            Assert.Equal(secondaryVersion, saved.PrefilledSecondaryPolicyModifiedUtc);
            Assert.Equal("Existing secondary", saved.SecondaryInsuranceCompanyName);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProfilePrefill_InsuranceRecoverySubmitsExplicitActionAndKeepsOtherAnswers(bool omit)
    {
        var patientId = Guid.NewGuid();
        var authorization = this.AddTestAuthorization();
        authorization.SetAuthorized("synthetic-pt");
        authorization.SetRoles(Roles.PT);
        var initial = new IntakeResponseDraft
        {
            PatientId = patientId,
            IntakeFlowVersion = 2,
            CurrentStep = (int)IntakeStep.PainAssessment,
            InsuranceCompanyName = "Stale",
            MemberOrPolicyNumber = "OLD",
            MedicalHistoryNotes = "Keep me",
            ProfileSourceRevision = Guid.NewGuid(),
            ProfilePrefill = new IntakeProfilePrefill { InsuranceCompanyName = "Current", MemberOrPolicyNumber = "NEW", Fingerprint = "current-offer" }
        };
        var service = CreateIntakeServiceMock(initial);
        IntakeResponseDraft? accepted = null;
        service.Setup(value => value.SaveDraftAsync(It.IsAny<IntakeResponseDraft>(), It.IsAny<CancellationToken>()))
            .Callback<IntakeResponseDraft, CancellationToken>((draft, _) =>
            {
                Assert.Equal(omit ? IntakeProfileAction.OmitPrimary : IntakeProfileAction.UsePrimary, draft.ProfileUpdate!.Action);
                Assert.Equal("current-offer", draft.ProfileUpdate.ExpectedFingerprint);
                Assert.Equal(initial.ProfileSourceRevision, draft.ProfileSourceRevision);
                Assert.Equal("Keep me", draft.MedicalHistoryNotes);
                draft.InsuranceCompanyName = omit ? null : "Current";
                draft.MemberOrPolicyNumber = omit ? null : "NEW";
                draft.OmitPrimaryPolicy = omit;
                draft.ProfileUpdate = null;
                draft.ProfileSourceRevision = Guid.NewGuid();
                accepted = draft;
            }).Returns(Task.CompletedTask);
        service.Setup(value => value.GetLatestByPatientIdAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => accepted ?? initial);
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/intake/{patientId}");
        var cut = RenderPage(patientId);
        cut.Find(omit ? "[data-testid='intake-omit-primary']" : "[data-testid='intake-use-primary']").Click();
        cut.WaitForAssertion(() => Assert.Equal(omit ? null : "NEW", GetWizardState(cut).MemberOrPolicyNumber));
        Assert.Equal("Keep me", GetWizardState(cut).MedicalHistoryNotes);
        Assert.Equal(omit, GetWizardState(cut).OmitPrimaryPolicy);
        Assert.Equal(accepted!.ProfileSourceRevision, GetWizardState(cut).ProfileSourceRevision);
    }

    [Fact]
    public void ProfileAcceptance_FailedReloadBlocksWritesUntilSavedAnswersAreRecovered()
    {
        var patientId = Guid.NewGuid();
        var authorization = this.AddTestAuthorization();
        authorization.SetAuthorized("synthetic-pt");
        authorization.SetRoles(Roles.PT);
        var initial = CreateSubmittableReviewDraft(patientId);
        initial.InsuranceCompanyName = "Stale";
        initial.ProfilePrefill = new IntakeProfilePrefill { InsuranceCompanyName = "Current", Fingerprint = "offer" };
        var recovered = CreateSubmittableReviewDraft(patientId);
        recovered.InsuranceCompanyName = "Current";
        var service = CreateIntakeServiceMock(initial);
        var saved = false;
        var reloadFails = true;
        service.Setup(value => value.SaveDraftAsync(It.IsAny<IntakeResponseDraft>(), It.IsAny<CancellationToken>()))
            .Callback(() => saved = true).Returns(Task.CompletedTask);
        service.Setup(value => value.GetLatestByPatientIdAsync(patientId, It.IsAny<CancellationToken>()))
            .Returns(() => saved && reloadFails
                ? Task.FromException<IntakeResponseDraft?>(new HttpRequestException("Reload unavailable"))
                : Task.FromResult<IntakeResponseDraft?>(saved
                    ? recovered
                    : initial));
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/intake/{patientId}");
        var cut = RenderPage(patientId);

        cut.Find("[data-testid='intake-use-primary']").Click();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid='intake-profile-reload']")));
        Assert.True(cut.Find("fieldset").HasAttribute("disabled"));
        Assert.Empty(cut.FindAll("[data-testid='intake-use-primary']"));
        service.Verify(value => value.SaveDraftAsync(It.IsAny<IntakeResponseDraft>(), It.IsAny<CancellationToken>()), Times.Once);
        service.Verify(value => value.SubmitAsync(It.IsAny<IntakeResponseDraft>(), It.IsAny<CancellationToken>()), Times.Never);
        reloadFails = false;
        cut.Find("[data-testid='intake-reload-saved']").Click();
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid='intake-profile-reload']")));
        Assert.Equal("Current", GetWizardState(cut).InsuranceCompanyName);
        Assert.Equal(IntakeStep.Review, GetWizardState(cut).CurrentStep);
        Assert.False(cut.Find("fieldset").HasAttribute("disabled"));
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
    public void LoadFailure_ForSamePatient_DoesNotRetryOnRerender()
    {
        var patientId = Guid.NewGuid();
        var authorization = this.AddTestAuthorization();
        authorization.SetAuthorized("pt-user");
        authorization.SetRoles(Roles.PT);

        var intakeService = new Mock<IIntakeService>(MockBehavior.Strict);
        intakeService
            .Setup(service => service.GetLatestByPatientIdAsync(
                patientId,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Intake API unavailable.", null, HttpStatusCode.ServiceUnavailable));

        Services.AddLogging();
        Services.AddSingleton<IHeaderConfigurationService, HeaderConfigurationService>();
        Services.AddSingleton<IIntakeDemographicsValidationService, IntakeDemographicsValidationService>();
        Services.AddSingleton(intakeService.Object);
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/intake/{patientId}");

        var cut = RenderPage(patientId);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Intake API unavailable.", cut.Markup, StringComparison.Ordinal);
            Assert.Single(Services.GetRequiredService<IToastService>().GetAll());
        });

        cut.FindComponent<IntakeWizardPage>().Render();

        intakeService.Verify(
            service => service.GetLatestByPatientIdAsync(
                patientId,
                It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.Single(Services.GetRequiredService<IToastService>().GetAll());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SubmitSuccess_ShowsInlineMessageAndSuccessToast(bool patientEncounter)
    {
        var patientId = Guid.NewGuid();
        var authorization = this.AddTestAuthorization();
        authorization.SetAuthorized("pt-user");
        authorization.SetRoles(patientEncounter ? Roles.Patient : Roles.PT);

        var draft = patientEncounter ? SubmittableEncounterDraft(patientId) : CreateSubmittableReviewDraft(patientId);
        var intakeService = CreateIntakeServiceMock(draft);
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
    public void SubmitInvalidOperation_ShowsInlineMessageAndErrorToast()
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
            .ThrowsAsync(new InvalidOperationException("Cannot submit intake; intake is already locked."));

        Services.GetRequiredService<NavigationManager>().NavigateTo($"/intake/{patientId}");

        var cut = RenderPage(patientId);

        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid='submit-button']")));
        cut.Find("[data-testid='submit-button']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Cannot submit intake; intake is already locked.", cut.Markup, StringComparison.Ordinal);
            var toast = Assert.Single(Services.GetRequiredService<IToastService>().GetAll());
            Assert.Equal(ToastLevel.Error, toast.Level);
            Assert.Equal("Cannot submit intake; intake is already locked.", toast.Message);
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
            PainSeverityScore = 4,
            PainSeverityProvided = true,
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

        Assert.NotNull(stateProperty);
        return Assert.IsType<IntakeWizardState>(stateProperty.GetValue(component.Instance));
    }
}

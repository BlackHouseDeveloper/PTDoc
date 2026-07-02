using Bunit;
using Microsoft.Extensions.DependencyInjection;
using PTDoc.Application.DTOs;
using PTDoc.UI.Components.Patients.Profile;
using PTDoc.UI.Components.Patients.Profile.Models;
using PTDoc.UI.Services;
using System.Reflection;

namespace PTDoc.Tests.UI.Patients;

[Trait("Category", "CoreCi")]
public sealed class PatientClinicalInfoCardEditableTests : TestContext
{
    private readonly FakePatientChartStorageService chartStorageService = new();

    public PatientClinicalInfoCardEditableTests()
    {
        Services.AddSingleton<IPatientChartStorageService>(chartStorageService);
    }

    [Fact]
    public void TimelineAndNotesTabs_UseStableTabSemanticsAndPanels()
    {
        var cut = RenderComponent<PatientClinicalInfoCardEditable>(parameters => parameters
            .Add(component => component.Patient, CreatePatient())
            .Add(component => component.TimelineEntries, new[]
            {
                new TimelineEntry
                {
                    Type = "note",
                    Title = "Evaluation signed",
                    Description = "Initial evaluation was signed.",
                    Date = "May 25",
                    Time = "9:00 AM"
                }
            })
            .Add(component => component.RecentNotes, new[]
            {
                new NoteListItemApiResponse
                {
                    Id = Guid.NewGuid(),
                    PatientId = Guid.NewGuid(),
                    NoteType = "Daily",
                    DateOfService = new DateTime(2026, 5, 25),
                    IsSigned = false
                }
            }));

        var tabList = cut.Find("[data-testid='patient-profile-tabs']");
        Assert.Equal("tablist", tabList.GetAttribute("role"));
        Assert.Equal("tab", cut.Find("[data-testid='patient-profile-tab-timeline']").GetAttribute("role"));
        Assert.Equal("tab", cut.Find("[data-testid='patient-profile-tab-documents']").GetAttribute("role"));
        Assert.Equal("tab", cut.Find("[data-testid='patient-profile-tab-communications']").GetAttribute("role"));
        Assert.Equal("true", cut.Find("[data-testid='patient-profile-tab-timeline']").GetAttribute("aria-selected"));
        Assert.Equal("patient-profile-panel-current", cut.Find("[data-testid='patient-profile-tab-timeline']").GetAttribute("aria-controls"));
        Assert.Equal("tabpanel", cut.Find("[data-testid='patient-profile-panel-timeline']").GetAttribute("role"));
        Assert.Equal("patient-profile-panel-current", cut.Find("[data-testid='patient-profile-panel-timeline']").Id);
        Assert.Contains("Evaluation signed", cut.Markup, StringComparison.Ordinal);

        cut.Find("[data-testid='patient-profile-tab-notes']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("false", cut.Find("[data-testid='patient-profile-tab-timeline']").GetAttribute("aria-selected"));
            Assert.Equal("true", cut.Find("[data-testid='patient-profile-tab-notes']").GetAttribute("aria-selected"));
            Assert.Equal("patient-profile-panel-current", cut.Find("[data-testid='patient-profile-tab-notes']").GetAttribute("aria-controls"));
            Assert.Equal("patient-profile-tab-notes", cut.Find("[data-testid='patient-profile-panel-notes']").GetAttribute("aria-labelledby"));
            Assert.Equal("patient-profile-panel-current", cut.Find("[data-testid='patient-profile-panel-notes']").Id);
            Assert.Contains("Daily", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void DocumentsAndCommunicationsTabs_RenderExplicitWorkflowStates()
    {
        var cut = RenderComponent<PatientClinicalInfoCardEditable>(parameters => parameters
            .Add(component => component.Patient, CreatePatient()));

        cut.Find("[data-testid='patient-profile-tab-documents']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("true", cut.Find("[data-testid='patient-profile-tab-documents']").GetAttribute("aria-selected"));
            Assert.Equal("patient-profile-tab-documents", cut.Find("[data-testid='patient-profile-panel-documents']").GetAttribute("aria-labelledby"));
            Assert.Contains("Patient Documents", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Insurance card", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Upload document", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("No patient documents have been uploaded", cut.Markup, StringComparison.Ordinal);
            Assert.NotNull(cut.Find("#patient-document-file"));
        });

        cut.Find("[data-testid='patient-profile-tab-communications']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("true", cut.Find("[data-testid='patient-profile-tab-communications']").GetAttribute("aria-selected"));
            Assert.Equal("patient-profile-tab-communications", cut.Find("[data-testid='patient-profile-panel-communications']").GetAttribute("aria-labelledby"));
            Assert.Contains("Communication Log", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Portal", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("No patient communications have been logged", cut.Markup, StringComparison.Ordinal);
            Assert.Contains(
                cut.FindAll("button"),
                button => button.TextContent.Contains("Add communication", StringComparison.Ordinal) && !button.HasAttribute("disabled"));
        });
    }

    [Fact]
    public void CommunicationsTab_InputBeforeClick_CreatesStoredCommunicationEntry()
    {
        var cut = RenderComponent<PatientClinicalInfoCardEditable>(parameters => parameters
            .Add(component => component.Patient, CreatePatient()));

        cut.Find("[data-testid='patient-profile-tab-communications']").Click();
        cut.Find("#patient-communication-contact").Input("Local QA");
        cut.Find("#patient-communication-summary").Input("Called patient about referral status.");
        cut.Find("#patient-communication-details").Input("Synthetic communication log storage validation.");
        cut.FindAll("button").Single(button => button.TextContent.Contains("Add communication", StringComparison.Ordinal)).Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Communication logged.", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Called patient about referral status.", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Synthetic communication log storage validation.", cut.Markup, StringComparison.Ordinal);
            Assert.Contains(chartStorageService.CreatedCommunicationLogEntries, entry => entry.Summary == "Called patient about referral status.");
        });
    }

    [Fact]
    public async Task LoadDocumentsAsync_WhenRefreshFails_ClearsStaleDocumentStatus()
    {
        var cut = RenderComponent<PatientClinicalInfoCardEditable>(parameters => parameters
            .Add(component => component.Patient, CreatePatient()));
        cut.Find("[data-testid='patient-profile-tab-documents']").Click();
        cut.WaitForAssertion(() => Assert.Contains("Patient Documents", cut.Markup, StringComparison.Ordinal));
        SetPrivateProperty(cut.Instance, "DocumentStatusMessage", "Document uploaded.");
        chartStorageService.FailDocumentList = true;

        await cut.InvokeAsync(() => InvokePrivateLoadAsync(cut.Instance, "LoadDocumentsAsync"));
        cut.Render();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Unable to load patient documents.", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Document uploaded.", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void LoadDocumentsAsync_WhenPatientIdIsInvalid_ClearsStaleDocumentStatus()
    {
        var cut = RenderComponent<PatientClinicalInfoCardEditable>(parameters => parameters
            .Add(component => component.Patient, new PatientProfileVm
            {
                Id = "not-a-guid",
                DisplayName = "Legacy Patient"
            }));
        SetPrivateProperty(cut.Instance, "DocumentStatusMessage", "Document uploaded.");

        cut.Find("[data-testid='patient-profile-tab-documents']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Patient document storage is unavailable for this chart.", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Document uploaded.", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task LoadCommunicationsAsync_WhenRefreshFails_ClearsStaleCommunicationStatus()
    {
        var cut = RenderComponent<PatientClinicalInfoCardEditable>(parameters => parameters
            .Add(component => component.Patient, CreatePatient()));
        cut.Find("[data-testid='patient-profile-tab-communications']").Click();
        cut.WaitForAssertion(() => Assert.Contains("Communication Log", cut.Markup, StringComparison.Ordinal));
        SetPrivateProperty(cut.Instance, "CommunicationStatusMessage", "Communication logged.");
        chartStorageService.FailCommunicationList = true;

        await cut.InvokeAsync(() => InvokePrivateLoadAsync(cut.Instance, "LoadCommunicationsAsync"));
        cut.Render();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Unable to load patient communications.", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Communication logged.", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void CreateCommunicationAsync_WhenSummaryIsBlank_ClearsStaleCommunicationStatus()
    {
        var cut = RenderComponent<PatientClinicalInfoCardEditable>(parameters => parameters
            .Add(component => component.Patient, CreatePatient()));
        cut.Find("[data-testid='patient-profile-tab-communications']").Click();
        cut.WaitForAssertion(() => Assert.Contains("Communication Log", cut.Markup, StringComparison.Ordinal));
        SetPrivateProperty(cut.Instance, "CommunicationStatusMessage", "Communication logged.");

        cut.FindAll("button").Single(button => button.TextContent.Contains("Add communication", StringComparison.Ordinal)).Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Summary is required.", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Communication logged.", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void TimelineError_RendersOnlyErrorState()
    {
        var cut = RenderComponent<PatientClinicalInfoCardEditable>(parameters => parameters
            .Add(component => component.Patient, CreatePatient())
            .Add(component => component.TimelineErrorMessage, "Unable to load timeline.")
            .Add(component => component.TimelineEntries, new[]
            {
                new TimelineEntry
                {
                    Type = "note",
                    Title = "Should not render while errored",
                    Description = "Stale data",
                    Date = "May 25",
                    Time = "9:00 AM"
                }
            }));

        Assert.Contains("Unable to load timeline.", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Should not render while errored", cut.Markup, StringComparison.Ordinal);
    }

    private static PatientProfileVm CreatePatient() => new()
    {
        Id = Guid.NewGuid().ToString("D"),
        DisplayName = "Alex Patient"
    };

    private static void SetPrivateProperty(
        PatientClinicalInfoCardEditable component,
        string propertyName,
        string value)
    {
        var property = typeof(PatientClinicalInfoCardEditable).GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(property);
        property!.SetValue(component, value);
    }

    private static Task InvokePrivateLoadAsync(
        PatientClinicalInfoCardEditable component,
        string methodName)
    {
        var method = typeof(PatientClinicalInfoCardEditable).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (Task)method!.Invoke(component, [true])!;
    }

    private sealed class FakePatientChartStorageService : IPatientChartStorageService
    {
        private readonly List<PatientCommunicationLogEntryResponse> communicationLogEntries = new();

        public IReadOnlyList<PatientCommunicationLogEntryResponse> CreatedCommunicationLogEntries => communicationLogEntries;

        public bool FailDocumentList { get; set; }

        public bool FailCommunicationList { get; set; }

        public Task<IReadOnlyList<PatientDocumentResponse>> ListDocumentsAsync(
            Guid patientId,
            CancellationToken cancellationToken = default)
        {
            if (FailDocumentList)
            {
                throw new InvalidOperationException("Document list failed.");
            }

            return Task.FromResult<IReadOnlyList<PatientDocumentResponse>>(Array.Empty<PatientDocumentResponse>());
        }

        public Task<PatientDocumentResponse> UploadDocumentAsync(
            Guid patientId,
            Microsoft.AspNetCore.Components.Forms.IBrowserFile file,
            string documentType,
            string? notes,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PatientDocumentResponse
            {
                Id = Guid.NewGuid(),
                PatientId = patientId,
                DocumentType = documentType,
                FileName = file.Name,
                ContentType = file.ContentType,
                SizeBytes = file.Size,
                UploadedAtUtc = DateTime.UtcNow
            });

        public Task<IReadOnlyList<PatientCommunicationLogEntryResponse>> ListCommunicationLogEntriesAsync(
            Guid patientId,
            CancellationToken cancellationToken = default)
        {
            if (FailCommunicationList)
            {
                throw new InvalidOperationException("Communication list failed.");
            }

            return Task.FromResult<IReadOnlyList<PatientCommunicationLogEntryResponse>>(communicationLogEntries.ToArray());
        }

        public Task<PatientCommunicationLogEntryResponse> CreateCommunicationLogEntryAsync(
            Guid patientId,
            CreatePatientCommunicationLogEntryRequest request,
            CancellationToken cancellationToken = default)
        {
            var response = new PatientCommunicationLogEntryResponse
            {
                Id = Guid.NewGuid(),
                PatientId = patientId,
                Channel = request.Channel,
                Direction = request.Direction,
                Summary = request.Summary,
                Details = request.Details,
                ContactName = request.ContactName,
                OccurredAtUtc = request.OccurredAtUtc ?? DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow
            };

            communicationLogEntries.Add(response);

            return Task.FromResult(response);
        }
    }
}

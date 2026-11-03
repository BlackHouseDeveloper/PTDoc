using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PTDoc.Application.Auth;
using PTDoc.Application.DTOs;
using PTDoc.Application.Integrations;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.UI.Components.Layout;
using PTDoc.UI.Components.Notes;
using PTDoc.UI.Components.Notes.Models;
using PTDoc.UI.Components.Patients;
using PTDoc.UI.Pages;
using PTDoc.UI.Services;

namespace PTDoc.Tests.UI.Pages;

[Trait("Category", "CoreCi")]
public sealed class FaxCenterTests : TestContext
{
    private readonly Mock<IPatientService> patients = new();
    private readonly Mock<INoteService> notes = new();
    private readonly Mock<IIntegrationClientService> integration = new();
    private readonly Guid patientId = Guid.NewGuid();
    private readonly Guid noteId = Guid.NewGuid();
    private readonly DateTime version = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);

    public FaxCenterTests()
    {
        this.AddTestAuthorization().SetAuthorized("Synthetic clinician");
        Services.AddSingleton(patients.Object); Services.AddSingleton(notes.Object); Services.AddSingleton(integration.Object);
        Services.AddSingleton(Mock.Of<IToastService>());
        var documents = new Mock<IPatientChartStorageService>();
        documents.Setup(s => s.ListDocumentsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<PatientDocumentResponse>());
        Services.AddSingleton(documents.Object);
        ComponentFactories.AddStub<GlobalPageHeader>();
        patients.Setup(p => p.SearchPageAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<PatientListItemResponse>());
        patients.Setup(p => p.GetByIdAsync(patientId, It.IsAny<CancellationToken>())).ReturnsAsync(new PatientResponse { Id = patientId, FirstName = "Synthetic", LastName = "Fax" });
        notes.Setup(n => n.GetNotesAsync(patientId, null, null, 100, null, null, It.IsAny<CancellationToken>(), null, null, null, 0)).ReturnsAsync(Array.Empty<NoteListItemApiResponse>());
        notes.Setup(n => n.GetByIdAsync(noteId, It.IsAny<CancellationToken>())).ReturnsAsync(new NoteDetailResponse
        { Note = new NoteResponse { Id = noteId, PatientId = patientId, NoteStatus = NoteStatus.Signed, LastModifiedUtc = version, DateOfService = version.Date, NoteType = NoteType.Evaluation } });
        integration.Setup(i => i.GetFaxTransmissionsAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<FaxTransmissionResponse>());
        integration.Setup(i => i.GetInboundFaxesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<InboundFaxResponse>());
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    public void ReviewAction_RequiresSignedNoteAndFaxPermission(bool signed, bool allowed, bool expected)
    {
        var auth = this.AddTestAuthorization(); auth.SetAuthorized("Synthetic");
        auth.SetPolicies(allowed ? [AuthorizationPolicies.FaxSend] : []);
        var cut = RenderComponent<SoapReviewPage>(p => p.Add(x => x.Note, new SoapNoteVm { Status = signed ? NoteStatus.Signed : NoteStatus.Draft })
            .Add(x => x.FaxUrl, "/fax-center?synthetic=true"));
        Assert.Equal(expected, cut.FindAll("a").Any(a => a.TextContent.Contains("Fax signed note")));
    }

    [Fact]
    public void ContextBeyondNoteList_RequiresReviewThenExplicitSend_AndReusesIdAfterFailure()
    {
        var auth = this.AddTestAuthorization(); auth.SetAuthorized("Synthetic"); auth.SetPolicies(AuthorizationPolicies.FaxSend);
        var calls = new List<CreateFaxTransmissionRequest>();
        integration.Setup(i => i.QueueFaxAsync(It.IsAny<CreateFaxTransmissionRequest>(), It.IsAny<CancellationToken>()))
            .Returns((CreateFaxTransmissionRequest request, CancellationToken ct) =>
            {
                calls.Add(request);
                return calls.Count == 1 ? Task.FromException<FaxTransmissionResponse>(new HttpRequestException("Synthetic lost response"))
                    : Task.FromResult(new FaxTransmissionResponse { Id = request.RequestId!.Value });
            });
        var cut = RenderComponent<FaxCenterHarness>(p => p.Add(x => x.ContextPatient, patientId).Add(x => x.ContextNote, noteId).Add(x => x.ContextVersion, version.ToString("O")));
        cut.WaitForAssertion(() => Assert.Contains($"note:{noteId}", cut.Markup));
        cut.FindAll("label").Single(l => l.TextContent.Trim() == "Recipient fax number").QuerySelector("input")!.Change("2025550101");
        cut.FindAll("button").Single(b => b.TextContent == "Review fax").Click();
        Assert.Empty(calls);
        Assert.Contains("2025550101", cut.Find("section[aria-label='Review fax destination']").TextContent);
        cut.FindAll("button").Single(b => b.TextContent == "Confirm and queue fax").Click();
        cut.WaitForAssertion(() => Assert.Single(calls));
        cut.FindAll("button").Single(b => b.TextContent == "Retry same fax request").Click();
        cut.WaitForAssertion(() => Assert.Equal(2, calls.Count));
        Assert.Equal(calls[0].RequestId, calls[1].RequestId);
        Assert.Equal(version, calls[0].ExpectedNoteLastModifiedUtc);
        Assert.Equal(patientId, calls[0].PatientId); Assert.Equal(noteId, calls[0].ClinicalNoteId);
        cut.WaitForAssertion(() => Assert.Contains("queued does not mean delivered", cut.Markup));
    }

    [Fact]
    public void ChangedVersion_IsNotPreselected()
    {
        var auth = this.AddTestAuthorization(); auth.SetAuthorized("Synthetic"); auth.SetPolicies(AuthorizationPolicies.FaxSend);
        var cut = RenderComponent<FaxCenterHarness>(p => p.Add(x => x.ContextPatient, patientId).Add(x => x.ContextNote, noteId).Add(x => x.ContextVersion, version.AddMinutes(-1).ToString("O")));
        cut.WaitForAssertion(() => Assert.Contains("unavailable or changed", cut.Markup));
        Assert.DoesNotContain($"value=\"note:{noteId}\"", cut.Markup);
        integration.Verify(i => i.QueueFaxAsync(It.IsAny<CreateFaxTransmissionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void Selector_PagesBeyondFirst25_AndSearchRestartsAtFirstPage()
    {
        var rows = Enumerable.Range(0, 31).Select(i => new PatientListItemResponse { Id = Guid.NewGuid(), DisplayName = $"Synthetic {i:D2}" }).ToArray();
        patients.Setup(p => p.SearchPageAsync(It.IsAny<string>(), 26, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string q, int take, int skip, CancellationToken ct) => rows.Skip(skip).Take(take).ToArray());
        Guid? selected = null;
        var cut = RenderComponent<ClinicPatientSelector>(p => p.Add(x => x.ValueChanged, (Guid? id) => selected = id));
        cut.WaitForAssertion(() => Assert.Contains("Synthetic 24", cut.Markup));
        Assert.DoesNotContain("Synthetic 25", cut.Markup);
        cut.FindAll("button").Single(b => b.TextContent == "Next patients").Click();
        cut.WaitForAssertion(() => Assert.Contains("Synthetic 30", cut.Markup));
        cut.Find("select").Change(rows[30].Id.ToString());
        Assert.Equal(rows[30].Id, selected);
        cut.Find("input").Input("Synthetic");
        cut.FindAll("button").Single(b => b.TextContent == "Search").Click();
        cut.WaitForAssertion(() => Assert.Contains("Page 1", cut.Markup));
        patients.Verify(p => p.SearchPageAsync("Synthetic", 26, 0, It.IsAny<CancellationToken>()), Times.Once);
    }
    // This bUnit version does not supply query parameters through its router.
    // The real URL binding is covered by the local browser verification.
    public sealed class FaxCenterHarness : FaxCenter
    {
        [Parameter] public Guid? ContextPatient { get; set; }
        [Parameter] public Guid? ContextNote { get; set; }
        [Parameter] public string? ContextVersion { get; set; }
        protected override Task OnParametersSetAsync()
        {
            PatientId = ContextPatient; NoteId = ContextNote; Version = ContextVersion;
            return base.OnParametersSetAsync();
        }
    }

}

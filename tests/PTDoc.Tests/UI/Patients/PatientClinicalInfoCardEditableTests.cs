using Bunit;
using PTDoc.Application.DTOs;
using PTDoc.UI.Components.Patients.Profile;
using PTDoc.UI.Components.Patients.Profile.Models;

namespace PTDoc.Tests.UI.Patients;

[Trait("Category", "CoreCi")]
public sealed class PatientClinicalInfoCardEditableTests : TestContext
{
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
        Assert.Equal("true", cut.Find("[data-testid='patient-profile-tab-timeline']").GetAttribute("aria-selected"));
        Assert.Equal("patient-profile-panel-timeline", cut.Find("[data-testid='patient-profile-tab-timeline']").GetAttribute("aria-controls"));
        Assert.Equal("tabpanel", cut.Find("[data-testid='patient-profile-panel-timeline']").GetAttribute("role"));
        Assert.Contains("Evaluation signed", cut.Markup, StringComparison.Ordinal);

        cut.Find("[data-testid='patient-profile-tab-notes']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("false", cut.Find("[data-testid='patient-profile-tab-timeline']").GetAttribute("aria-selected"));
            Assert.Equal("true", cut.Find("[data-testid='patient-profile-tab-notes']").GetAttribute("aria-selected"));
            Assert.Equal("patient-profile-tab-notes", cut.Find("[data-testid='patient-profile-panel-notes']").GetAttribute("aria-labelledby"));
            Assert.Contains("Daily", cut.Markup, StringComparison.Ordinal);
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
}

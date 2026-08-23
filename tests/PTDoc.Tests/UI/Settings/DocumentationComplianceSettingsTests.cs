using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using PTDoc.UI.Components.Settings;

namespace PTDoc.Tests.UI.Settings;

[Trait("Category", "CoreCi")]
public sealed class DocumentationComplianceSettingsTests : TestContext
{
    [Fact]
    public void DefaultState_RendersDocumentedHardStopsAndUnavailableSaveExplanation()
    {
        var cut = RenderComponent<DocumentationComplianceSettings>();

        Assert.Equal("true", cut.Find("#hard-stops-tab").GetAttribute("aria-selected"));
        Assert.Equal(6, cut.FindAll("button[role='switch']").Count);
        Assert.Equal("2", cut.Find("#minimum-icd10-codes").GetAttribute("value"));
        Assert.Contains("Hard Stop Enforcement", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("saving remains unavailable", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.True(cut.Find(".documentation-settings__save-action").HasAttribute("disabled"));
    }

    [Fact]
    public void Tabs_RenderEveryApprovedPanelAndDefaultState()
    {
        var cut = RenderComponent<DocumentationComplianceSettings>();

        cut.Find("#progress-notes-tab").Click();
        Assert.Contains("Progress Note Automation", cut.Markup, StringComparison.Ordinal);
        Assert.Equal("both", cut.Find("input[name='progress-note-frequency']:checked").GetAttribute("value"));
        Assert.Equal("10", cut.Find("#progress-note-visit-interval").GetAttribute("value"));
        Assert.Equal("30", cut.Find("#progress-note-day-interval").GetAttribute("value"));

        cut.Find("#poc-tracking-tab").Click();
        Assert.Contains("POC Tracking Example", cut.Markup, StringComparison.Ordinal);
        Assert.Equal("14", cut.Find("#poc-warning-window").GetAttribute("value"));

        cut.Find("#note-behavior-tab").Click();
        Assert.Contains("Carry-Forward Example", cut.Markup, StringComparison.Ordinal);
        Assert.True(cut.Find("#carry-forward-subjective").HasAttribute("checked"));
        Assert.False(cut.Find("#carry-forward-assessment").HasAttribute("checked"));
        Assert.Equal("60", cut.Find("#auto-save-interval").GetAttribute("value"));
    }

    [Fact]
    public void LocalDraft_CanBeChangedAndCancelledWithoutPretendingToPersist()
    {
        var cut = RenderComponent<DocumentationComplianceSettings>();
        var toggle = cut.Find("button[aria-label='Require Minimum ICD-10 Codes']");

        toggle.Click();
        Assert.Equal("false", cut.Find("button[aria-label='Require Minimum ICD-10 Codes']").GetAttribute("aria-checked"));
        Assert.Contains("Unsaved preview changes", cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("#minimum-icd10-codes"));

        cut.FindAll(".documentation-settings__action-bar button")[1].Click();
        Assert.Equal("true", cut.Find("button[aria-label='Require Minimum ICD-10 Codes']").GetAttribute("aria-checked"));
        Assert.Contains("Preview changes were discarded", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void QuickAdminTools_NavigateToImplementedSettingsRoutes()
    {
        var cut = RenderComponent<DocumentationComplianceSettings>();

        cut.FindAll(".quick-admin-tools__row")[0].Click();
        Assert.EndsWith("/settings/auto-check-in", Services.GetRequiredService<NavigationManager>().Uri, StringComparison.Ordinal);
    }
}

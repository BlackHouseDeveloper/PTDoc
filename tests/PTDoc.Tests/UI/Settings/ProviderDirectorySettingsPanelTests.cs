using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PTDoc.Application.Providers;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.UI.Components.Settings;
using PTDoc.UI.Services;

namespace PTDoc.Tests.UI.Settings;

[Trait("Category", "CoreCi")]
public sealed class ProviderDirectorySettingsPanelTests : TestContext
{
    [Fact]
    public void CandidateSubmission_UsesValuesEnteredThroughInputEvents()
    {
        var providers = new Mock<IProviderDirectoryService>(MockBehavior.Strict);
        providers
            .Setup(service => service.SearchForAdministrationAsync(
                It.IsAny<string?>(),
                ProviderDirectoryStatus.Pending,
                100,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        providers
            .Setup(service => service.SearchForAdministrationAsync(
                It.IsAny<string?>(),
                ProviderDirectoryStatus.Active,
                100,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        SubmitProviderCandidateRequest? submitted = null;
        providers
            .Setup(service => service.SubmitAsync(It.IsAny<SubmitProviderCandidateRequest>(), It.IsAny<CancellationToken>()))
            .Callback<SubmitProviderCandidateRequest, CancellationToken>((request, _) => submitted = request)
            .ReturnsAsync(new ProviderDirectoryEntryDto
            {
                Id = Guid.NewGuid(),
                FirstName = "Avery",
                LastName = "Ng",
                DisplayName = "Avery Ng",
                Status = ProviderDirectoryStatus.Pending
            });

        Services.AddSingleton(providers.Object);
        Services.AddSingleton<IToastService, ToastService>();

        var cut = RenderComponent<ProviderDirectorySettingsPanel>();
        cut.WaitForAssertion(() => Assert.Contains("No provider candidates", cut.Markup, StringComparison.Ordinal));

        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Submit provider").Click();
        cut.FindAll(".provider-admin__form-grid input")[0].Input("Avery");
        cut.FindAll(".provider-admin__form-grid input")[1].Input("Ng");
        cut.FindAll(".provider-admin__form-grid input")[2].Input("DPT");
        cut.FindAll(".provider-admin__form-grid input")[3].Input("1234567890");
        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Submit for approval").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(submitted);
            Assert.Equal("Avery", submitted.FirstName);
            Assert.Equal("Ng", submitted.LastName);
            Assert.Equal("DPT", submitted.Credentials);
            Assert.Equal("1234567890", submitted.Npi);
            Assert.DoesNotContain("First and last name are required.", cut.Markup, StringComparison.Ordinal);
        });
        providers.Verify(
            service => service.SubmitAsync(It.IsAny<SubmitProviderCandidateRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void CandidateSubmission_BlankNamesRetainsRequiredValidation()
    {
        var providers = new Mock<IProviderDirectoryService>(MockBehavior.Strict);
        providers
            .Setup(service => service.SearchForAdministrationAsync(
                It.IsAny<string?>(),
                It.IsAny<ProviderDirectoryStatus?>(),
                100,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        Services.AddSingleton(providers.Object);
        Services.AddSingleton<IToastService, ToastService>();

        var cut = RenderComponent<ProviderDirectorySettingsPanel>();
        cut.WaitForElement(".provider-admin__toolbar");
        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Submit provider").Click();
        cut.FindAll("button").Single(button => button.TextContent.Trim() == "Submit for approval").Click();

        Assert.Contains("First and last name are required.", cut.Markup, StringComparison.Ordinal);
        providers.Verify(
            service => service.SubmitAsync(It.IsAny<SubmitProviderCandidateRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
    [Theory]
    [InlineData(false, ProviderDirectoryStatus.Active)]
    [InlineData(true, ProviderDirectoryStatus.Active)]
    [InlineData(false, ProviderDirectoryStatus.Pending)]
    public void DirectoryEdit_PreservesUnexposedFields_AndRetainsInputOnConflict(bool conflict, ProviderDirectoryStatus status)
    {
        var provider = new ProviderDirectoryEntryDto
        {
            Id = Guid.NewGuid(),
            FirstName = "Original",
            LastName = "Provider",
            DisplayName = "Original Provider",
            Fax = "2025550100",
            Email = "synthetic@example.test",
            AddressLine1 = "Historical address",
            TaxonomyCode = "test-code",
            SubmissionSource = ProviderSubmissionSource.PatientIntake,
            Status = ProviderDirectoryStatus.Active,
            LastModifiedUtc = DateTime.UtcNow
        };
        var providers = new Mock<IProviderDirectoryService>();
        providers.Setup(x => x.SearchForAdministrationAsync(It.IsAny<string?>(), ProviderDirectoryStatus.Pending, 100, default)).ReturnsAsync(status == ProviderDirectoryStatus.Pending ? [provider] : []);
        providers.Setup(x => x.SearchForAdministrationAsync(It.IsAny<string?>(), ProviderDirectoryStatus.Active, 100, default)).ReturnsAsync(status == ProviderDirectoryStatus.Active ? [provider] : []);
        UpdateProviderCandidateRequest? saved = null;
        var setup = providers.Setup(x => x.UpdateForAdministrationAsync(provider.Id, It.IsAny<UpdateProviderCandidateRequest>(), default))
            .Callback<Guid, UpdateProviderCandidateRequest, CancellationToken>((_, request, _) => saved = request);
        if (conflict) setup.ThrowsAsync(new InvalidOperationException("Changed in another session."));
        else setup.ReturnsAsync(provider);
        Services.AddSingleton(providers.Object);
        Services.AddSingleton<IToastService, ToastService>();
        var cut = RenderComponent<ProviderDirectorySettingsPanel>();
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Edit").Click();
        cut.FindAll(".provider-admin__form-grid input")[7].Input("2025550199");
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Save changes").Click();
        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(saved);
            Assert.Equal("2025550199", saved.Fax);
            Assert.Equal(provider.Email, saved.Email);
            Assert.Equal(provider.AddressLine1, saved.AddressLine1);
            Assert.Equal(provider.TaxonomyCode, saved.TaxonomyCode);
            Assert.Equal(provider.SubmissionSource, saved.SubmissionSource);
            Assert.Equal(provider.LastModifiedUtc, saved.ExpectedLastModifiedUtc);
            if (conflict)
            {
                Assert.Contains("Changed in another session", cut.Find("[role=alert]").TextContent);
                Assert.Equal("2025550199", cut.FindAll(".provider-admin__form-grid input")[7].GetAttribute("value"));
            }
            else Assert.Empty(cut.FindAll(".provider-admin__form-grid"));
        });
        Assert.Equal("2025550100", provider.Fax);
        providers.Verify(x => x.ApproveAsync(It.IsAny<Guid>(), It.IsAny<ProviderDecisionRequest>(), default), Times.Never);
    }

    [Fact]
    public void Delete_RequiresConfirmation_AndArchivesWithObservedVersion()
    {
        var provider = new ProviderDirectoryEntryDto { Id = Guid.NewGuid(), DisplayName = "Synthetic Provider", Status = ProviderDirectoryStatus.Active, LastModifiedUtc = DateTime.UtcNow };
        var providers = new Mock<IProviderDirectoryService>();
        providers.Setup(x => x.SearchForAdministrationAsync(It.IsAny<string?>(), ProviderDirectoryStatus.Pending, 100, default)).ReturnsAsync([]);
        providers.Setup(x => x.SearchForAdministrationAsync(It.IsAny<string?>(), ProviderDirectoryStatus.Active, 100, default)).ReturnsAsync([provider]);
        Services.AddSingleton(providers.Object);
        Services.AddSingleton<IToastService, ToastService>();
        var cut = RenderComponent<ProviderDirectorySettingsPanel>();
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Delete").Click();
        providers.Verify(x => x.ArchiveAsync(It.IsAny<Guid>(), It.IsAny<ProviderDecisionRequest>(), default), Times.Never);
        Assert.Contains("Existing patient links and saved clinical records remain available", cut.Markup);
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Delete from directory").Click();
        cut.WaitForAssertion(() => providers.Verify(x => x.ArchiveAsync(provider.Id,
            It.Is<ProviderDecisionRequest>(r => r.ExpectedLastModifiedUtc == provider.LastModifiedUtc), default), Times.Once));
    }

}

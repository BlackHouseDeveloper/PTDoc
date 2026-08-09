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
}

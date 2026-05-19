using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PTDoc.Application.Services;
using PTDoc.UI.Pages.PatientInfo;
using PTDoc.UI.Services;

namespace PTDoc.Tests.UI.Pages;

[Trait("Category", "CoreCi")]
public sealed class PatientInfoRouteTests : TestContext
{
    [Fact]
    public void MalformedPatientInfoId_RedirectsToPatients()
    {
        var patientService = new Mock<IPatientService>(MockBehavior.Strict);
        Services.AddSingleton(patientService.Object);
        Services.AddSingleton<IToastService, ToastService>();

        RenderComponent<PatientInfoPage>(parameters => parameters
            .Add(component => component.Id, "not-a-guid"));

        var navigation = Services.GetRequiredService<NavigationManager>();
        Assert.EndsWith("/patients", navigation.Uri, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingPatientInfoId_RedirectsToPatients()
    {
        var patientService = new Mock<IPatientService>(MockBehavior.Strict);
        Services.AddSingleton(patientService.Object);
        Services.AddSingleton<IToastService, ToastService>();

        RenderComponent<PatientInfoPage>(parameters => parameters
            .Add(component => component.Id, "   "));

        var navigation = Services.GetRequiredService<NavigationManager>();
        Assert.EndsWith("/patients", navigation.Uri, StringComparison.Ordinal);
        patientService.Verify(
            service => service.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void UnknownPatientId_ShowsPatientNotFoundRecoveryState()
    {
        var patientService = new Mock<IPatientService>(MockBehavior.Strict);
        patientService
            .Setup(service => service.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PTDoc.Application.DTOs.PatientResponse?)null);
        Services.AddSingleton(patientService.Object);
        Services.AddSingleton<IToastService, ToastService>();

        var cut = RenderComponent<PatientInfoPage>(parameters => parameters
            .Add(component => component.Id, Guid.NewGuid().ToString()));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Patient not found.", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Back to Patients", cut.Markup, StringComparison.Ordinal);
        });
    }
}

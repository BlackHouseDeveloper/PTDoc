using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PTDoc.Application.DTOs;
using PTDoc.Application.Services;
using PTDoc.UI.Components.Intake;
using Xunit;

namespace PTDoc.Tests.UI.Intake;

[Trait("Category", "CoreCi")]
public sealed class ClinicianPatientSelectorTests : TestContext
{
    [Fact]
    public void ClinicianPatientSelector_LoadsPatientsFromCanonicalSearchService()
    {
        var patientId = Guid.NewGuid();
        var patientService = new Mock<IPatientService>(MockBehavior.Strict);

        patientService
            .Setup(service => service.SearchAsync(
                It.Is<string?>(query => string.IsNullOrWhiteSpace(query)),
                100,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new PatientListItemResponse
                {
                    Id = patientId,
                    DisplayName = "Jordan Rivera",
                    MedicalRecordNumber = "MRN-1001"
                }
            ]);

        Services.AddSingleton(patientService.Object);

        var cut = RenderComponent<ClinicianPatientSelector>();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Jordan Rivera", cut.Markup, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("MRN-1001", cut.Markup, StringComparison.OrdinalIgnoreCase);
        });

        patientService.Verify(service => service.SearchAsync(
            It.Is<string?>(query => string.IsNullOrWhiteSpace(query)),
            100,
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void ClinicianPatientSelector_SearchesLivePatientService()
    {
        var patientService = new Mock<IPatientService>(MockBehavior.Strict);

        patientService
            .Setup(service => service.SearchAsync(
                It.Is<string?>(query => string.IsNullOrWhiteSpace(query)),
                100,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PatientListItemResponse>());

        patientService
            .Setup(service => service.SearchAsync(
                "smith",
                50,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new PatientListItemResponse
                {
                    Id = Guid.NewGuid(),
                    DisplayName = "Sam Smith",
                    MedicalRecordNumber = "MRN-2002"
                }
            ]);

        Services.AddSingleton(patientService.Object);

        var cut = RenderComponent<ClinicianPatientSelector>();

        cut.Find("#intake-patient-search").Input("smith");

        cut.WaitForAssertion(() =>
        {
            patientService.Verify(service => service.SearchAsync("smith", 50, It.IsAny<CancellationToken>()), Times.Once);
            Assert.Contains("Sam Smith", cut.Markup, StringComparison.OrdinalIgnoreCase);
        }, timeout: TimeSpan.FromSeconds(2));
    }
}

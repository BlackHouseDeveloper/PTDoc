using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PTDoc.Application.DTOs;
using PTDoc.Application.Services;
using PTDoc.UI.Pages.PatientInfo;
using PTDoc.UI.Services;
using System.Text.Json;

namespace PTDoc.Tests.UI.Pages;

[Trait("Category", "CoreCi")]
public sealed class PatientInfoRouteTests : TestContext
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

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

    [Fact]
    public void Save_PreservesStructuredAdjusterFieldsFromIntakePayerJson()
    {
        var patientId = Guid.NewGuid();
        var payerInfoJson = JsonSerializer.Serialize(new
        {
            insuranceCompanyName = "Primary Health",
            memberIdPolicyNumber = "PRI-123",
            secondaryInsuranceCompanyName = "Secondary Health",
            secondaryMemberIdPolicyNumber = "SEC-123",
            secondaryGroupNumber = "SEC-GRP",
            adjusterName = "Alex Adjuster",
            adjusterPhone = "555-0200",
            adjusterEmail = "adjuster@example.com",
            adjusterFax = "555-0201"
        }, SerializerOptions);
        UpdatePatientRequest? capturedRequest = null;

        var patient = new PatientResponse
        {
            Id = patientId,
            FirstName = "Beta",
            LastName = "Patient",
            DateOfBirth = new DateTime(1990, 1, 1),
            PayerInfoJson = payerInfoJson
        };

        var patientService = new Mock<IPatientService>(MockBehavior.Strict);
        patientService
            .Setup(service => service.GetByIdAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(patient);
        patientService
            .Setup(service => service.UpdateAsync(patientId, It.IsAny<UpdatePatientRequest>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, UpdatePatientRequest, CancellationToken>((_, request, _) => capturedRequest = request)
            .ReturnsAsync(patient);
        Services.AddSingleton(patientService.Object);
        Services.AddSingleton<IToastService, ToastService>();

        var cut = RenderComponent<PatientInfoPage>(parameters => parameters
            .Add(component => component.Id, patientId.ToString()));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Secondary Health", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("Alex Adjuster", cut.Markup, StringComparison.Ordinal);
        });

        cut.Find("#pi-insurance-name").Input("Updated Primary Health");
        cut.Find("button[aria-label='Save Changes']").Click();

        cut.WaitForAssertion(() => Assert.NotNull(capturedRequest?.PayerInfoJson));

        using var savedPayerInfo = JsonDocument.Parse(capturedRequest!.PayerInfoJson!);
        var root = savedPayerInfo.RootElement;
        Assert.Equal("Alex Adjuster", root.GetProperty("adjusterName").GetString());
        Assert.Equal("555-0200", root.GetProperty("adjusterPhone").GetString());
        Assert.Equal("adjuster@example.com", root.GetProperty("adjusterEmail").GetString());
        Assert.Equal("555-0201", root.GetProperty("adjusterFax").GetString());
        Assert.Equal("SEC-GRP", root.GetProperty("secondaryGroupNumber").GetString());
    }
}

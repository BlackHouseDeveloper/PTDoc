using System.Net;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PTDoc.Application.DTOs;
using PTDoc.Application.Services;
using PTDoc.UI.Components.Patients.Profile;
using PTDoc.UI.Components.Patients.Profile.Models;
using PTDoc.UI.Services;

namespace PTDoc.Tests.UI.Patients;

[Trait("Category", "CoreCi")]
public sealed class PatientDemographicsEditorTests : TestContext
{
    private readonly Mock<IPatientService> service = new();
    private readonly PatientProfileVm patient = new()
    {
        Id = Guid.NewGuid().ToString(),
        DisplayName = "Synthetic Patient",
        DateOfBirth = "1/2/1980",
        Email = "synthetic@example.test",
        PhoneNumber = "2025550101",
        AddressLine1 = "Old Street",
        AddressLine2 = "Unit 2",
        City = "Old City",
        State = "MD",
        ZipCode = "20001",
        EmergencyContactName = "Old Contact",
        EmergencyContactPhone = "2025550102",
        LastModifiedUtc = DateTime.UtcNow
    };

    public PatientDemographicsEditorTests()
    {
        Services.AddSingleton(service.Object);
        Services.AddSingleton<IToastService, ToastService>();
    }

    [Fact]
    public void SaveSendsExistingVersionAndContactFieldsWithoutMutatingInputOrUnrelatedFields()
    {
        UpdatePatientRequest? saved = null;
        var reloads = 0;
        service.Setup(x => x.UpdateAsync(It.IsAny<Guid>(), It.IsAny<UpdatePatientRequest>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, UpdatePatientRequest, CancellationToken>((_, request, _) => saved = request)
            .ReturnsAsync(new PatientResponse());
        var cut = RenderComponent<PatientDemographicsCardEditable>(p => p.Add(x => x.Patient, patient).Add(x => x.OnPatientUpdated, () => reloads++));
        cut.Find("button[aria-label='Edit patient information']").Click();
        cut.Find("#edit-phone").Change("2025550111");
        cut.Find("#edit-address1").Change("New Street");
        cut.Find("#edit-address2").Change("");
        cut.Find("#edit-city").Change("New City");
        cut.Find("#edit-state").Change("VA");
        cut.Find("#edit-postal").Change("22001");
        cut.Find("#edit-emergency-name").Change("New Contact");
        cut.Find("#edit-emergency-phone").Change("2025550112");
        cut.Find(".patient-demographics-save-btn").Click();
        cut.WaitForAssertion(() => Assert.Equal(1, reloads));
        Assert.NotNull(saved);
        Assert.Equal(patient.LastModifiedUtc, saved.ExpectedLastModifiedUtc);
        Assert.Equal("2025550111", saved.Phone);
        Assert.Equal("New Street", saved.AddressLine1);
        Assert.Equal("", saved.AddressLine2);
        Assert.Equal("New City", saved.City);
        Assert.Equal("VA", saved.State);
        Assert.Equal("22001", saved.ZipCode);
        Assert.Equal("New Contact", saved.EmergencyContactName);
        Assert.Equal("2025550112", saved.EmergencyContactPhone);
        Assert.Equal(patient.Email, saved.Email);
        Assert.Null(saved.AuthorizationNumber);
        Assert.Null(saved.PayerInfoJson);
        Assert.Null(saved.ConsentSigned);
        Assert.Equal("Old Street", patient.AddressLine1);
    }

    [Theory]
    [InlineData("edit-name", "", "name is required")]
    [InlineData("edit-dob", "not-a-date", "valid date of birth")]
    [InlineData("edit-email", "not-an-email", "valid email")]
    public void InvalidInputPreservesEditsWithoutCallingApi(string field, string value, string message)
    {
        var cut = RenderComponent<PatientDemographicsCardEditable>(p => p.Add(x => x.Patient, patient));
        cut.Find("button[aria-label='Edit patient information']").Click();
        cut.Find("#edit-phone").Change("2025550123");
        cut.Find("#" + field).Change(value);
        cut.Find(".patient-demographics-save-btn").Click();
        Assert.Contains(message, cut.Find("[role='alert']").TextContent);
        Assert.Equal("2025550123", cut.Find("#edit-phone").GetAttribute("value"));
        service.Verify(x => x.UpdateAsync(It.IsAny<Guid>(), It.IsAny<UpdatePatientRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void ConflictRetainsEditsAndOriginalVersion()
    {
        service.Setup(x => x.UpdateAsync(It.IsAny<Guid>(), It.IsAny<UpdatePatientRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Conflict", null, HttpStatusCode.Conflict));
        var cut = RenderComponent<PatientDemographicsCardEditable>(p => p.Add(x => x.Patient, patient));
        cut.Find("button[aria-label='Edit patient information']").Click();
        cut.Find("#edit-phone").Change("2025550123");
        cut.Find(".patient-demographics-save-btn").Click();
        cut.WaitForAssertion(() => Assert.Contains("Your edits are retained", cut.Find("[role='alert']").TextContent));
        Assert.Equal("2025550123", cut.Find("#edit-phone").GetAttribute("value"));
        Assert.Equal("2025550101", patient.PhoneNumber);
    }
}

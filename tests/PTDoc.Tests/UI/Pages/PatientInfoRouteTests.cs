using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PTDoc.Application.DTOs;
using PTDoc.Application.Services;
using PTDoc.UI.Components.PatientInfo;
using PTDoc.UI.Components.PatientInfo.Models;
using PTDoc.UI.Pages.PatientInfo;
using PTDoc.UI.Services;
using System.Globalization;
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
    public async Task AuthorizationReferralHistoryPanel_AddHistoryEntry_RendersEditableRow()
    {
        var entries = new List<AuthorizationReferralHistoryEntryVm>();
        var cut = RenderComponent<AuthorizationReferralHistoryPanel>(parameters => parameters
            .Add(component => component.Model, entries)
            .Add(component => component.ModelChanged, updated => entries = updated));

        await cut.InvokeAsync(() => cut.FindAll("button")
            .Single(button => button.TextContent.Contains("Add history entry", StringComparison.Ordinal))
            .Click());

        cut.WaitForElement("#pi-auth-history-0-start");
        Assert.Single(entries);
    }

    [Fact]
    public void AuthorizationReferralHistoryPanel_NewModelInstance_ReplacesLocalEntries()
    {
        var firstEntries = new List<AuthorizationReferralHistoryEntryVm>
        {
            new()
            {
                EntryId = "first-entry",
                ReferenceNumber = "AUTH-OLD"
            }
        };
        var replacementEntries = new List<AuthorizationReferralHistoryEntryVm>
        {
            new()
            {
                EntryId = "replacement-entry",
                ReferenceNumber = "AUTH-NEW"
            }
        };

        var cut = RenderComponent<AuthorizationReferralHistoryPanel>(parameters => parameters
            .Add(component => component.Model, firstEntries));

        cut.Find("#pi-auth-history-0-number").Input("AUTH-LOCAL");

        cut.SetParametersAndRender(parameters => parameters
            .Add(component => component.Model, replacementEntries));

        Assert.DoesNotContain("AUTH-OLD", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("AUTH-LOCAL", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("AUTH-NEW", cut.Markup, StringComparison.Ordinal);
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

    [Fact]
    public async Task Save_PersistsCostSharingVisitLimitsAndAuthorizationReferralHistory()
    {
        var patientId = Guid.NewGuid();
        UpdatePatientRequest? capturedRequest = null;

        var patient = new PatientResponse
        {
            Id = patientId,
            FirstName = "Beta",
            LastName = "Patient",
            DateOfBirth = new DateTime(1990, 1, 1),
            PayerInfoJson = "{}"
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
            Assert.Contains("Authorization / PCP Referral Required?", cut.Markup, StringComparison.Ordinal));

        cut.Find("#pi-auth-required").Change("Yes");
        cut.Find("#pi-auth-type").Change("pcp_referral");
        cut.Find("#pi-auth-number").Input("REF-123");
        cut.Find("#pi-deductible-amount").Input("1500");
        cut.Find("#pi-deductible-met").Input("250");
        cut.Find("#pi-oop-max").Input("6000");
        cut.Find("#pi-oop-met").Input("750");
        cut.Find("#pi-copay-amount").Input("35");
        cut.Find("#pi-coinsurance-percent").Input("20");
        cut.Find("#pi-visit-limit-type").Change("visits");
        cut.Find("#pi-visit-limit-period").Change("authorization_period");
        cut.Find("#pi-total-visit-limit").Input("20");
        await cut.InvokeAsync(() => cut.FindAll("button")
            .Single(button => button.TextContent.Contains("Add history entry", StringComparison.Ordinal))
            .Click());
        cut.WaitForElement("#pi-auth-history-0-type");
        cut.Find("#pi-auth-history-0-type").Change("PCP Referral");
        cut.Find("#pi-auth-history-0-number").Input("REF-HIST-1");
        cut.Find("#pi-auth-history-0-status").Change("Active");
        cut.Find("#pi-auth-history-0-start").Input("2026-01-01");
        cut.Find("#pi-auth-history-0-end").Input("2026-03-31");
        cut.Find("#pi-auth-history-0-units").Input("12");
        cut.Find("#pi-auth-history-0-notes").Input("Initial PCP referral note.");

        cut.Find("button[aria-label='Save Changes']").Click();

        cut.WaitForAssertion(() => Assert.NotNull(capturedRequest?.PayerInfoJson));

        using var savedPayerInfo = JsonDocument.Parse(capturedRequest!.PayerInfoJson!);
        var root = savedPayerInfo.RootElement;
        Assert.Equal("1500", root.GetProperty("deductibleAmount").GetString());
        Assert.Equal("250", root.GetProperty("deductibleMet").GetString());
        Assert.Equal("6000", root.GetProperty("outOfPocketMaximum").GetString());
        Assert.Equal("750", root.GetProperty("outOfPocketMet").GetString());
        Assert.Equal("35", root.GetProperty("copayAmount").GetString());
        Assert.Equal("20", root.GetProperty("coinsurancePercent").GetString());
        Assert.Equal("visits", root.GetProperty("visitLimitType").GetString());
        Assert.Equal("authorization_period", root.GetProperty("visitLimitPeriod").GetString());
        Assert.Equal("20", root.GetProperty("totalVisitLimit").GetString());

        var history = root.GetProperty("authorizationReferralHistory");
        Assert.Equal("PCP Referral", history[0].GetProperty("recordType").GetString());
        Assert.Equal("REF-HIST-1", history[0].GetProperty("referenceNumber").GetString());
        Assert.Equal("Active", history[0].GetProperty("status").GetString());
        Assert.Equal("2026-01-01", history[0].GetProperty("startDate").GetString());
        Assert.Equal("2026-03-31", history[0].GetProperty("endDate").GetString());
        Assert.Equal("12", history[0].GetProperty("visitsOrUnitsAuthorized").GetString());
        Assert.Equal("Initial PCP referral note.", history[0].GetProperty("notes").GetString());
    }

    [Fact]
    public async Task AuthorizationReferralHistory_OverlappingDates_DisablesSave()
    {
        var patientId = Guid.NewGuid();
        var patient = new PatientResponse
        {
            Id = patientId,
            FirstName = "Beta",
            LastName = "Patient",
            DateOfBirth = new DateTime(1990, 1, 1),
            PayerInfoJson = "{}"
        };

        var patientService = new Mock<IPatientService>(MockBehavior.Strict);
        patientService
            .Setup(service => service.GetByIdAsync(patientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(patient);
        Services.AddSingleton(patientService.Object);
        Services.AddSingleton<IToastService, ToastService>();

        var cut = RenderComponent<PatientInfoPage>(parameters => parameters
            .Add(component => component.Id, patientId.ToString()));

        cut.WaitForAssertion(() =>
            Assert.Contains("Authorization / PCP Referral History", cut.Markup, StringComparison.Ordinal));

        var addButton = cut.FindAll("button").Single(button => button.TextContent.Contains("Add history entry", StringComparison.Ordinal));
        await cut.InvokeAsync(() => addButton.Click());
        cut.WaitForElement("#pi-auth-history-0-start");
        await cut.InvokeAsync(() => cut.FindAll("button")
            .Single(button => button.TextContent.Contains("Add history entry", StringComparison.Ordinal))
            .Click());
        cut.WaitForElement("#pi-auth-history-1-start");
        cut.Find("#pi-auth-history-0-start").Input("2026-01-01");
        cut.Find("#pi-auth-history-0-end").Input("2026-03-31");
        cut.Find("#pi-auth-history-1-start").Input("2026-03-01");
        cut.Find("#pi-auth-history-1-end").Input("2026-04-30");

        Assert.Contains("Authorization / PCP referral history has date or numeric issues", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("History date ranges cannot overlap.", cut.Markup, StringComparison.Ordinal);
        Assert.True(cut.Find("button[aria-label='Save Changes']").HasAttribute("disabled"));
    }

    [Fact]
    public async Task AuthorizationReferralHistory_DateValidation_UsesInvariantCulture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUICulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-GB");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-GB");

        try
        {
            var patientId = Guid.NewGuid();
            var patient = new PatientResponse
            {
                Id = patientId,
                FirstName = "Beta",
                LastName = "Patient",
                DateOfBirth = new DateTime(1990, 1, 1),
                PayerInfoJson = "{}"
            };

            var patientService = new Mock<IPatientService>(MockBehavior.Strict);
            patientService
                .Setup(service => service.GetByIdAsync(patientId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(patient);
            Services.AddSingleton(patientService.Object);
            Services.AddSingleton<IToastService, ToastService>();

            var cut = RenderComponent<PatientInfoPage>(parameters => parameters
                .Add(component => component.Id, patientId.ToString()));

            cut.WaitForAssertion(() =>
                Assert.Contains("Authorization / PCP Referral History", cut.Markup, StringComparison.Ordinal));

            var addButton = cut.FindAll("button")
                .Single(button => button.TextContent.Contains("Add history entry", StringComparison.Ordinal));
            await cut.InvokeAsync(() => addButton.Click());
            cut.WaitForElement("#pi-auth-history-0-start");
            await cut.InvokeAsync(() => cut.FindAll("button")
                .Single(button => button.TextContent.Contains("Add history entry", StringComparison.Ordinal))
                .Click());
            cut.WaitForElement("#pi-auth-history-1-start");

            cut.Find("#pi-auth-history-0-start").Input("2026-03-01");
            cut.Find("#pi-auth-history-0-end").Input("2026-03-31");
            cut.Find("#pi-auth-history-1-start").Input("03/15/2026");
            cut.Find("#pi-auth-history-1-end").Input("04/01/2026");

            Assert.DoesNotContain("Use a valid date.", cut.Markup, StringComparison.Ordinal);
            Assert.Contains("History date ranges cannot overlap.", cut.Markup, StringComparison.Ordinal);
            Assert.True(cut.Find("button[aria-label='Save Changes']").HasAttribute("disabled"));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUICulture;
        }
    }
}

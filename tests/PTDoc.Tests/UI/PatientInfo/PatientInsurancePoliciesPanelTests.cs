using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PTDoc.Application.Insurance;
using PTDoc.Application.ReferenceData;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.ReferenceData;
using PTDoc.UI.Components.PatientInfo;
using PTDoc.UI.Services;

namespace PTDoc.Tests.UI.PatientInfo;

[Trait("Category", "CoreCi")]
public sealed class PatientInsurancePoliciesPanelTests : TestContext
{
    [Fact]
    public void Tricare_RendersAndSurvivesPolicyEditing()
    {
        var patientId = Guid.NewGuid();
        var policy = new InsurancePolicyDto
        {
            Id = Guid.NewGuid(),
            PatientId = patientId,
            CoveragePriority = InsuranceCoveragePriority.Primary,
            Status = InsurancePolicyStatus.Active,
            CarrierDisplayName = "Military Plan",
            PayerType = InsurancePayerType.Tricare
        };
        var insurance = new Mock<IInsurancePolicyService>(MockBehavior.Strict);
        insurance.Setup(service => service.ListAsync(patientId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync([policy]);
        insurance.Setup(service => service.UpsertPolicyAsync(patientId, policy.Id,
                It.Is<UpsertInsurancePolicyRequest>(request => request.PayerType == InsurancePayerType.Tricare),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(policy);
        Services.AddSingleton(insurance.Object);
        Services.AddSingleton<IIntakeReferenceDataCatalogService, IntakeReferenceDataCatalogService>();
        Services.AddSingleton<IToastService, ToastService>();
        var cut = RenderComponent<PatientInsurancePoliciesPanel>(parameters => parameters.Add(component => component.PatientId, patientId));
        cut.WaitForAssertion(() => Assert.Contains("TRICARE", cut.Find("section[aria-labelledby='current-insurance-policies-title']").TextContent));
        cut.FindAll("button").Single(button => button.TextContent == "Edit").Click();
        var select = cut.FindAll("label").Single(label => label.TextContent.StartsWith("Payer type", StringComparison.Ordinal)).QuerySelector("select")!;
        Assert.Contains(select.QuerySelectorAll("option"), option => option.GetAttribute("value") == "Tricare" && option.TextContent == "TRICARE");
        select.Change("Commercial");
        cut.FindAll("label").Single(label => label.TextContent.StartsWith("Payer type", StringComparison.Ordinal)).QuerySelector("select")!.Change("Tricare");
        cut.FindAll("button").Single(button => button.TextContent == "Save policy").Click();
        cut.WaitForAssertion(() => insurance.Verify(service => service.UpsertPolicyAsync(patientId, policy.Id,
            It.Is<UpsertInsurancePolicyRequest>(request => request.PayerType == InsurancePayerType.Tricare),
            It.IsAny<CancellationToken>()), Times.Once));
    }

    [Fact]
    public void ArchivedPolicy_RendersInReadOnlyHistorySection()
    {
        var patientId = Guid.NewGuid();
        var insurance = new Mock<IInsurancePolicyService>(MockBehavior.Strict);
        insurance
            .Setup(service => service.ListAsync(patientId, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new InsurancePolicyDto
                {
                    Id = Guid.NewGuid(),
                    PatientId = patientId,
                    CoveragePriority = InsuranceCoveragePriority.Primary,
                    CarrierDisplayName = "Historical Carrier",
                    PayerType = InsurancePayerType.Tricare,
                    Status = InsurancePolicyStatus.Inactive,
                    IsArchived = true,
                    EffectiveStartDate = new DateTime(2025, 1, 1),
                    EffectiveEndDate = new DateTime(2025, 12, 31)
                }
            ]);
        Services.AddSingleton(insurance.Object);
        Services.AddSingleton<IIntakeReferenceDataCatalogService, IntakeReferenceDataCatalogService>();
        Services.AddSingleton<IToastService, ToastService>();

        var cut = RenderComponent<PatientInsurancePoliciesPanel>(parameters => parameters
            .Add(component => component.PatientId, patientId));

        cut.WaitForAssertion(() =>
        {
            var history = cut.Find("section[aria-labelledby='insurance-policy-history-title']");
            Assert.Contains("Policy history", history.TextContent, StringComparison.Ordinal);
            Assert.Contains("Historical Carrier", history.TextContent, StringComparison.Ordinal);
            Assert.Contains("Inactive", history.TextContent, StringComparison.Ordinal);
            Assert.Contains("TRICARE", history.TextContent, StringComparison.Ordinal);
            Assert.Empty(history.QuerySelectorAll("button"));
        });
    }
}

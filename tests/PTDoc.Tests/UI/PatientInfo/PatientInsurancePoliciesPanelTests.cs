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
                    PayerType = InsurancePayerType.Commercial,
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
            Assert.Empty(history.QuerySelectorAll("button"));
        });
    }
}

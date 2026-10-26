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
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnifiedSave_PassesOnlyEditedHistory_AndRetainsInputsOnFailure(bool fail)
    {
        var patientId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        var policy = new InsurancePolicyDto
        {
            Id = Guid.NewGuid(),
            PatientId = patientId,
            CarrierDisplayName = "Synthetic Carrier",
            CoveragePriority = InsuranceCoveragePriority.Primary,
            Status = InsurancePolicyStatus.Active,
            LastModifiedUtc = DateTime.UtcNow,
            TotalVisitLimit = 20,
            VisitLimitType = "visits",
            Authorizations = [new() { Id = childId, ReferenceNumber = "REF-1", Status = InsuranceAuthorizationStatus.Expired,
                AuthorizationType = InsuranceAuthorizationType.Referral, UsedUnits = 3, LastModifiedUtc = DateTime.UtcNow },
                new() { Id = Guid.NewGuid(), ReferenceNumber = "UNTOUCHED", LastModifiedUtc = DateTime.UtcNow }]
        };
        var insurance = new Mock<IInsurancePolicyService>();
        insurance.Setup(x => x.ListAsync(patientId, true, default)).ReturnsAsync([policy]);
        SaveInsurancePolicyWorkflowRequest? request = null;
        var setup = insurance.Setup(x => x.SaveWorkflowAsync(patientId, policy.Id, It.IsAny<SaveInsurancePolicyWorkflowRequest>(), default))
            .Callback<Guid, Guid?, SaveInsurancePolicyWorkflowRequest, CancellationToken>((_, _, value, _) => request = value);
        if (fail) setup.ThrowsAsync(new InvalidOperationException("Changed in another session. None saved."));
        else setup.ReturnsAsync(policy);
        Services.AddSingleton(insurance.Object);
        Services.AddSingleton<IIntakeReferenceDataCatalogService, IntakeReferenceDataCatalogService>();
        Services.AddSingleton<IToastService, ToastService>();
        var cut = RenderComponent<PatientInsurancePoliciesPanel>(parameters => parameters.Add(c => c.PatientId, patientId));
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Edit policy and referrals").Click();
        var first = cut.FindAll(".insurance-policies__authorization")[0];
        first.QuerySelector("input[type=text]")!.Input("REF-CORRECTED");
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Save policy and referrals").Click();
        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(request);
            Assert.Equal(20, request.TotalVisitLimit);
            Assert.Equal(policy.LastModifiedUtc, request.Policy.ExpectedLastModifiedUtc);
            var changed = Assert.Single(request.Authorizations);
            Assert.Equal(childId, changed.Id);
            Assert.Equal("REF-CORRECTED", changed.Values.ReferenceNumber);
            Assert.Equal(3, changed.Values.UsedUnits);
            Assert.Equal(InsuranceAuthorizationStatus.Expired, changed.Values.Status);
            if (fail)
            {
                Assert.Contains("None saved", cut.Find("[role=alert]").TextContent);
                Assert.Equal("REF-CORRECTED", cut.Find(".insurance-policies__authorization input[type=text]").GetAttribute("value"));
            }
            else Assert.Empty(cut.FindAll(".insurance-policies__editor"));
        });
        Assert.Equal("REF-1", policy.Authorizations[0].ReferenceNumber);
        insurance.Verify(x => x.UpsertAuthorizationAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<UpsertInsuranceAuthorizationRequest>(), default), Times.Never);
    }

}

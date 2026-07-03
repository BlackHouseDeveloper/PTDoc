using Bunit;
using Microsoft.AspNetCore.Components;
using PTDoc.UI.Components.Notes.Models;
using PTDoc.UI.Components.Notes.Workspace.DryNeedling;
using Xunit;

namespace PTDoc.Tests.UI.Notes;

[Trait("Category", "CoreCi")]
public sealed class DryNeedlingDocumentationComponentsTests : TestContext
{
    [Fact]
    public void DryNeedlingNoteView_CapturesBillingDesignation()
    {
        var vm = new DryNeedlingVm
        {
            DateOfTreatment = new DateTime(2026, 4, 16),
            Location = "Hip",
            NeedlingType = "Deep dry needling"
        };

        var cut = RenderComponent<DryNeedlingNoteView>(parameters => parameters
            .Add(component => component.Vm, vm)
            .Add(component => component.VmChanged, EventCallback.Factory.Create<DryNeedlingVm>(this, updated => vm = updated))
            .Add(component => component.IsReadOnly, false));

        cut.Find("#dry-billing-designation").Change("Non-billable");

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Non-billable", vm.BillingDesignation);
            Assert.Contains("Billing Designation", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void DryNeedlingTreatmentDetails_NormalizesBlankBillingDesignationWithoutMutatingParameter()
    {
        var billingDesignation = string.Empty;
        var changed = false;

        var cut = RenderComponent<DryNeedlingTreatmentDetails>(parameters => parameters
            .Add(component => component.BillingDesignation, billingDesignation)
            .Add(component => component.BillingDesignationChanged, EventCallback.Factory.Create<string>(
                this,
                updated =>
                {
                    billingDesignation = updated;
                    changed = true;
                }))
            .Add(component => component.ResponseDescription, string.Empty)
            .Add(component => component.ResponseDescriptionChanged, EventCallback.Factory.Create<string>(this, _ => { }))
            .Add(component => component.OnChanged, EventCallback.Factory.Create(this, () => { })));

        cut.Find("#dry-response").Change("No adverse response.");

        cut.WaitForAssertion(() =>
        {
            Assert.True(changed);
            Assert.Equal("Billable", billingDesignation);
            Assert.Equal(string.Empty, cut.Instance.BillingDesignation);
        });
    }
}

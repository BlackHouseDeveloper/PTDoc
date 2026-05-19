using Bunit;
using PTDoc.UI.Components;

namespace PTDoc.Tests.UI;

[Trait("Category", "CoreCi")]
public sealed class AddPatientModalTests : TestContext
{
    [Fact]
    public void Submit_WithEmptyRequiredFields_ShowsVisibleValidationAndDoesNotCallSubmit()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var submitCalled = false;

        var cut = RenderComponent<AddPatientModal>(parameters => parameters
            .Add(component => component.IsOpen, true)
            .Add(component => component.OnSubmit, _ =>
            {
                submitCalled = true;
                return Task.FromResult(true);
            }));

        cut.Find("form").Submit();

        Assert.False(submitCalled);
        Assert.Contains("Review required patient details.", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("First name is required.", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Last name is required.", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Email address is required.", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("Date of birth is required.", cut.Markup, StringComparison.Ordinal);
        Assert.Equal("true", cut.Find("#dob").GetAttribute("aria-invalid"));
    }

    [Fact]
    public void Submit_WithInvalidEmail_ShowsEmailValidationAndDoesNotCallSubmit()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var submitCalled = false;

        var cut = RenderComponent<AddPatientModal>(parameters => parameters
            .Add(component => component.IsOpen, true)
            .Add(component => component.OnSubmit, _ =>
            {
                submitCalled = true;
                return Task.FromResult(true);
            }));

        cut.Find("#firstName").Change("Alex");
        cut.Find("#lastName").Change("Patient");
        cut.Find("#email").Change("not-an-email");
        cut.Find("#dob").Change("1990-01-01");
        cut.Find("form").Submit();

        Assert.False(submitCalled);
        Assert.Contains("Enter a valid email address.", cut.Markup, StringComparison.Ordinal);
        Assert.Equal("true", cut.Find("#email").GetAttribute("aria-invalid"));
    }

    [Fact]
    public void Submit_WhenParentFails_KeepsModalOpenAndPreservesForm()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = RenderComponent<AddPatientModal>(parameters => parameters
            .Add(component => component.IsOpen, true)
            .Add(component => component.OnSubmit, _ => Task.FromResult(false)));

        cut.Find("#firstName").Change("Alex");
        cut.Find("#lastName").Change("Patient");
        cut.Find("#email").Change("alex.patient@example.com");
        cut.Find("#dob").Change("1990-01-01");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("The patient was not created.", cut.Markup, StringComparison.Ordinal);
            Assert.Equal("Alex", cut.Find("#firstName").GetAttribute("value"));
            Assert.NotEmpty(cut.FindAll(".modal-container"));
        });
    }

    [Fact]
    public void Submit_WhenParentSucceeds_ClosesModalAndRaisesStateChange()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var isOpenChanged = true;

        var cut = RenderComponent<AddPatientModal>(parameters => parameters
            .Add(component => component.IsOpen, true)
            .Add(component => component.IsOpenChanged, value => isOpenChanged = value)
            .Add(component => component.OnSubmit, _ => Task.FromResult(true)));

        cut.Find("#firstName").Change("Alex");
        cut.Find("#lastName").Change("Patient");
        cut.Find("#email").Change("alex.patient@example.com");
        cut.Find("#dob").Change("1990-01-01");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.False(isOpenChanged);
            Assert.Empty(cut.FindAll(".modal-container"));
        });
    }
}

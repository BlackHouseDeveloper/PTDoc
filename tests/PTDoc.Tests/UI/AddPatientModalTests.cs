using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
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
    public void Submit_WhenParentThrows_ShowsSafeErrorAndDoesNotExposeExceptionMessage()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = RenderComponent<AddPatientModal>(parameters => parameters
            .Add(component => component.IsOpen, true)
            .Add(component => component.OnSubmit, _ =>
                throw new InvalidOperationException("Raw backend failure for Jane Smith")));

        cut.Find("#firstName").Change("Alex");
        cut.Find("#lastName").Change("Patient");
        cut.Find("#email").Change("alex.patient@example.com");
        cut.Find("#dob").Change("1990-01-01");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("The patient was not created. Review the details and try again.", cut.Markup, StringComparison.Ordinal);
            Assert.DoesNotContain("Jane Smith", cut.Markup, StringComparison.Ordinal);
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

    [Fact]
    public void DateOfBirthInput_PreservesPartialYearWhileTyping()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;

        var cut = RenderComponent<AddPatientModal>(parameters => parameters
            .Add(component => component.IsOpen, true)
            .Add(component => component.OnSubmit, _ => Task.FromResult(true)));

        cut.Find("#dob").Input("2");

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("2", cut.Find("#dob").GetAttribute("value"));
            Assert.Empty(cut.FindAll(".field-error"));
        });
    }

    [Fact]
    public void Submit_WithInvalidDateOfBirth_ShowsDateValidationAndDoesNotCallSubmit()
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
        cut.Find("#email").Change("alex.patient@example.com");
        cut.Find("#dob").Input("not-a-date");
        cut.Find("form").Submit();

        Assert.False(submitCalled);
        Assert.Contains("Enter a valid date of birth.", cut.Markup, StringComparison.Ordinal);
        Assert.Equal("true", cut.Find("#dob").GetAttribute("aria-invalid"));
    }

    [Theory]
    [InlineData("2")]
    [InlineData("20")]
    [InlineData("02/13/2001")]
    public void Submit_WithNonIsoDateOfBirth_ShowsDateValidationAndDoesNotCallSubmit(string dateOfBirth)
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
        cut.Find("#email").Change("alex.patient@example.com");
        cut.Find("#dob").Input(dateOfBirth);
        cut.Find("form").Submit();

        Assert.False(submitCalled);
        Assert.Contains("Enter a valid date of birth.", cut.Markup, StringComparison.Ordinal);
        Assert.Equal("true", cut.Find("#dob").GetAttribute("aria-invalid"));
    }

    [Fact]
    public void AddPatientAndSendIntake_SubmitsCombinedIntent()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        AddPatientModal.PatientSubmitIntent? submittedIntent = null;

        var cut = RenderComponent<AddPatientModal>(parameters => parameters
            .Add(component => component.IsOpen, true)
            .Add(component => component.OnSubmit, formData =>
            {
                submittedIntent = formData.SubmitIntent;
                return Task.FromResult(true);
            }));

        cut.Find("#firstName").Change("Alex");
        cut.Find("#lastName").Change("Patient");
        cut.Find("#email").Change("alex.patient@example.com");
        cut.Find("#dob").Change("1990-01-01");
        cut.FindAll("button")
            .Single(button => button.TextContent.Contains("Add Patient + Send Intake", StringComparison.Ordinal))
            .Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(AddPatientModal.PatientSubmitIntent.AddPatientAndSendIntake, submittedIntent);
        });
    }

    [Fact]
    public void RerenderWhileOpen_DoesNotRefocusFirstName()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        SetupModalJsModule();

        var cut = RenderComponent<AddPatientModal>(parameters => parameters
            .Add(component => component.IsOpen, true)
            .Add(component => component.OnSubmit, _ => Task.FromResult(true)));

        cut.WaitForAssertion(() => Assert.True(CountFocusInvocations() > 0));
        var focusCount = CountFocusInvocations();

        cut.Find("#lastName").Change("Patient");

        cut.WaitForAssertion(() => Assert.Equal(focusCount, CountFocusInvocations()));
    }

    [Fact]
    public void ModalJsImport_WhenFirstAttemptIsCanceled_RetriesOnNextRender()
    {
        var jsRuntime = new RetryableModalJsRuntime();
        Services.AddSingleton<IJSRuntime>(jsRuntime);

        var cut = RenderComponent<AddPatientModal>(parameters => parameters
            .Add(component => component.IsOpen, true)
            .Add(component => component.OnSubmit, _ => Task.FromResult(true)));

        cut.WaitForAssertion(() => Assert.Equal(1, jsRuntime.ImportAttempts));

        cut.Render();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(2, jsRuntime.ImportAttempts);
            Assert.Contains("lockBodyScroll", jsRuntime.ModuleInvocations);
            Assert.Contains("registerEscapeHandler", jsRuntime.ModuleInvocations);
        });
    }

    private int CountFocusInvocations()
        => JSInterop.Invocations.Count(invocation =>
            invocation.Identifier.Contains("focus", StringComparison.OrdinalIgnoreCase));

    private void SetupModalJsModule()
        => JSInterop.SetupModule("./_content/PTDoc.UI/js/modal.js");

    private sealed class RetryableModalJsRuntime : IJSRuntime
    {
        private readonly RecordingJsObjectReference module;

        public RetryableModalJsRuntime()
        {
            module = new RecordingJsObjectReference(ModuleInvocations);
        }

        public int ImportAttempts { get; private set; }

        public List<string> ModuleInvocations { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (identifier == "import")
            {
                ImportAttempts++;
                if (ImportAttempts == 1)
                {
                    throw new OperationCanceledException();
                }

                return ValueTask.FromResult((TValue)(object)module);
            }

            return ValueTask.FromResult(default(TValue)!);
        }
    }

    private sealed class RecordingJsObjectReference(List<string> invocations) : IJSObjectReference
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            invocations.Add(identifier);
            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

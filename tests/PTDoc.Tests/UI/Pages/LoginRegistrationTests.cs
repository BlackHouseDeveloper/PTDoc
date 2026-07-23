using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PTDoc.Application.Auth;
using PTDoc.Application.Identity;
using PTDoc.Application.Services;
using LoginPage = PTDoc.UI.Pages.Login;

namespace PTDoc.Tests.UI.Pages;

[Trait("Category", "CoreCi")]
public sealed class LoginRegistrationTests : TestContext
{
    [Fact]
    public void SignUp_TextInputEventsPopulateTheModelBeforeImmediateSubmission()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var clinicId = Guid.NewGuid();
        var userService = CreateUserService(clinicId);
        userService
            .Setup(service => service.RegisterAsync(
                "Casey Tester",
                "casey.tester@example.com",
                new DateTime(1990, 1, 1),
                "PT",
                clinicId,
                "1234",
                "PT-1001",
                "MA",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegistrationResult(RegistrationStatus.PendingApproval, Guid.NewGuid(), null));

        RegisterServices(userService.Object);
        var cut = RenderComponent<LoginPage>();

        cut.FindAll("button.auth-tab")[1].Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("form[data-testid='signup-form']")));

        cut.Find("#fullName").Input("Casey Tester");
        cut.Find("#dateOfBirth").Input("1990-01-01");
        cut.Find("#email").Input("casey.tester@example.com");
        cut.Find("#roleKey").Change("PT");
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("#licenseNumber")));
        cut.Find("#clinicId").Change(clinicId.ToString());
        cut.Find("#pinSignup").Input("1234");
        cut.Find("#confirmPinSignup").Input("1234");
        cut.Find("#licenseNumber").Input("PT-1001");
        cut.Find("#licenseState").Change("MA");

        cut.Find("form[data-testid='signup-form']").Submit();

        cut.WaitForAssertion(() =>
        {
            userService.Verify(service => service.RegisterAsync(
                "Casey Tester",
                "casey.tester@example.com",
                new DateTime(1990, 1, 1),
                "PT",
                clinicId,
                "1234",
                "PT-1001",
                "MA",
                It.IsAny<CancellationToken>()), Times.Once);
            Assert.Contains("Registration submitted", cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void SignUp_InvalidSubmissionMarksAndDescribesTheFirstRequiredField()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var userService = CreateUserService(Guid.NewGuid());
        RegisterServices(userService.Object);
        var cut = RenderComponent<LoginPage>();

        cut.FindAll("button.auth-tab")[1].Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("form[data-testid='signup-form']")));

        cut.Find("form[data-testid='signup-form']").Submit();

        cut.WaitForAssertion(() =>
        {
            var fullName = cut.Find("#fullName");
            Assert.Equal("true", fullName.GetAttribute("aria-invalid"));
            Assert.Contains("fullName-validation", fullName.GetAttribute("aria-describedby"), StringComparison.Ordinal);
            Assert.NotEmpty(cut.FindAll("[data-testid='signup-validation-summary']"));
            Assert.NotEmpty(cut.FindAll("#fullName-validation[role='alert']"));
        });

        userService.Verify(
            service => service.RegisterAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<string>(),
                It.IsAny<Guid?>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void SignUp_ServerValidationErrorIsAppliedToTheMatchingField()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var clinicId = Guid.NewGuid();
        var userService = CreateUserService(clinicId);
        userService
            .Setup(service => service.RegisterAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<string>(),
                It.IsAny<Guid?>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegistrationResult(
                RegistrationStatus.ValidationFailed,
                null,
                "Registration data is incomplete.",
                new Dictionary<string, string[]>
                {
                    ["Email"] = ["A valid email address is required."]
                }));

        RegisterServices(userService.Object);
        var cut = RenderComponent<LoginPage>();

        cut.FindAll("button.auth-tab")[1].Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("form[data-testid='signup-form']")));
        cut.Find("#fullName").Input("Casey Tester");
        cut.Find("#dateOfBirth").Input("1990-01-01");
        cut.Find("#email").Input("casey.tester@example.com");
        cut.Find("#roleKey").Change("Owner");
        cut.Find("#clinicId").Change(clinicId.ToString());
        cut.Find("#pinSignup").Input("1234");
        cut.Find("#confirmPinSignup").Input("1234");

        cut.Find("form[data-testid='signup-form']").Submit();

        cut.WaitForAssertion(() =>
        {
            var email = cut.Find("#email");
            Assert.Equal("true", email.GetAttribute("aria-invalid"));
            Assert.Contains("email-validation", email.GetAttribute("aria-describedby"), StringComparison.Ordinal);
            Assert.Contains("A valid email address is required.", cut.Find("#email-validation").TextContent, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void SignUp_EditingAFieldClearsItsServerValidationError()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var clinicId = Guid.NewGuid();
        var userService = CreateUserService(clinicId);
        userService
            .Setup(service => service.RegisterAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<string>(),
                It.IsAny<Guid?>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RegistrationResult(
                RegistrationStatus.ValidationFailed,
                null,
                "Registration data is incomplete.",
                new Dictionary<string, string[]>
                {
                    ["Email"] = ["A valid email address is required."]
                }));

        RegisterServices(userService.Object);
        var cut = RenderComponent<LoginPage>();

        cut.FindAll("button.auth-tab")[1].Click();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("form[data-testid='signup-form']")));
        cut.Find("#fullName").Input("Casey Tester");
        cut.Find("#dateOfBirth").Input("1990-01-01");
        cut.Find("#email").Input("casey.tester@example.com");
        cut.Find("#roleKey").Change("Owner");
        cut.Find("#clinicId").Change(clinicId.ToString());
        cut.Find("#pinSignup").Input("1234");
        cut.Find("#confirmPinSignup").Input("1234");
        cut.Find("form[data-testid='signup-form']").Submit();

        cut.WaitForAssertion(() =>
            Assert.Contains("A valid email address is required.", cut.Find("#email-validation").TextContent, StringComparison.Ordinal));

        cut.Find("#email").Input("corrected@example.com");

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("false", cut.Find("#email").GetAttribute("aria-invalid"));
            Assert.Empty(cut.FindAll("#email-validation"));
        });
    }

    private void RegisterServices(IUserService userService)
    {
        Services.AddSingleton(userService);
        Services.AddLogging();
        Services.AddSingleton<IThemeService>(new TestThemeService());
    }

    private static Mock<IUserService> CreateUserService(Guid clinicId)
    {
        var userService = new Mock<IUserService>(MockBehavior.Loose);
        userService.SetupGet(service => service.SupportsSelfServiceRegistration).Returns(true);
        userService.SetupGet(service => service.SupportsExternalIdentityLogin).Returns(false);
        userService
            .Setup(service => service.GetClinicsForSignupAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ClinicSummary(clinicId, "Audit Clinic")]);
        userService
            .Setup(service => service.GetRolesForSignupAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new RoleSummary("PT", "Physical Therapist"),
                new RoleSummary("PTA", "Physical Therapist Assistant"),
                new RoleSummary("Owner", "Owner")
            ]);
        return userService;
    }

    private sealed class TestThemeService : IThemeService
    {
        public ThemeMode Current => ThemeMode.Light;
        public bool IsDarkMode => false;
        public event Action? OnThemeChanged
        {
            add { }
            remove { }
        }

        public Task InitializeAsync() => Task.CompletedTask;
        public Task ToggleAsync() => Task.CompletedTask;
        public Task SetThemeAsync(ThemeMode theme) => Task.CompletedTask;
        public Task ToggleThemeAsync() => Task.CompletedTask;
    }
}

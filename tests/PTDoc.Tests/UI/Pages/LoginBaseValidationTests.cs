using System.ComponentModel.DataAnnotations;
using System.Reflection;
using PTDoc.UI.Pages;

namespace PTDoc.Tests.UI.Pages;

[Trait("Category", "CoreCi")]
public sealed class LoginBaseValidationTests
{
    [Theory]
    [InlineData("email", "", "Email address is required.")]
    [InlineData("email", "not-an-email", "Enter a valid email address.")]
    [InlineData("sms", "", "Mobile number is required.")]
    [InlineData("sms", "123", "Enter a valid mobile number.")]
    public void ForgotPasswordModel_UsesChannelSpecificValidation(
        string channel,
        string contact,
        string expectedMessage)
    {
        var forgotPasswordModelType = typeof(LoginBase).GetNestedType("ForgotPasswordModel", BindingFlags.NonPublic);
        Assert.NotNull(forgotPasswordModelType);

        var forgotPasswordModel = Activator.CreateInstance(forgotPasswordModelType!);
        Assert.NotNull(forgotPasswordModel);

        forgotPasswordModelType!.GetProperty("Channel")!.SetValue(forgotPasswordModel, channel);
        forgotPasswordModelType.GetProperty("Contact")!.SetValue(forgotPasswordModel, contact);

        var validationResults = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(
            forgotPasswordModel!,
            new ValidationContext(forgotPasswordModel!),
            validationResults,
            validateAllProperties: true);

        Assert.False(isValid);
        Assert.Contains(validationResults, result => result.ErrorMessage == expectedMessage);
    }

    [Theory]
    [InlineData("Owner")]
    [InlineData("FrontDesk")]
    [InlineData("Billing")]
    [InlineData("Patient")]
    public void SignUpModel_NonLicensedRoles_DoNotRequireHiddenLicenseNumber(string roleKey)
    {
        var signUpModelType = typeof(LoginBase).GetNestedType("SignUpModel", BindingFlags.NonPublic);
        Assert.NotNull(signUpModelType);

        var signUpModel = Activator.CreateInstance(signUpModelType!);
        Assert.NotNull(signUpModel);

        signUpModelType!.GetProperty("FullName")!.SetValue(signUpModel, "Casey Tester");
        signUpModelType.GetProperty("DateOfBirth")!.SetValue(signUpModel, new DateTime(1990, 1, 1));
        signUpModelType.GetProperty("Email")!.SetValue(signUpModel, "casey@example.com");
        signUpModelType.GetProperty("RoleKey")!.SetValue(signUpModel, roleKey);
        signUpModelType.GetProperty("ClinicId")!.SetValue(signUpModel, Guid.NewGuid());
        signUpModelType.GetProperty("Pin")!.SetValue(signUpModel, "1234");
        signUpModelType.GetProperty("ConfirmPin")!.SetValue(signUpModel, "1234");
        signUpModelType.GetProperty("LicenseNumber")!.SetValue(signUpModel, string.Empty);
        signUpModelType.GetProperty("LicenseState")!.SetValue(signUpModel, string.Empty);

        var validationResults = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(
            signUpModel!,
            new ValidationContext(signUpModel!),
            validationResults,
            validateAllProperties: true);

        Assert.True(isValid, string.Join("; ", validationResults.Select(result => result.ErrorMessage)));
        Assert.DoesNotContain(
            validationResults,
            result => result.MemberNames.Contains("LicenseNumber", StringComparer.Ordinal));
    }

    [Fact]
    public void AuthPages_DoNotRenderDecorativeEmojiTextNodes()
    {
        var repoRoot = FindRepoRoot();
        var loginMarkup = File.ReadAllText(Path.Combine(repoRoot, "src/PTDoc.UI/Pages/Login.razor"));
        var resetMarkup = File.ReadAllText(Path.Combine(repoRoot, "src/PTDoc.UI/Pages/ResetPassword.razor"));
        var combined = loginMarkup + resetMarkup;

        Assert.DoesNotContain("\u2139\uFE0F", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("\ud83d\udd12", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("\u23F3", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("\u24D8", combined, StringComparison.Ordinal);
    }

    [Fact]
    public void WebHeadOutlet_IsInteractive_ForClientSideTitleUpdates()
    {
        var repoRoot = FindRepoRoot();
        var appMarkup = File.ReadAllText(Path.Combine(repoRoot, "src/PTDoc.Web/Components/App.razor"));

        Assert.Contains("<HeadOutlet @rendermode=\"InteractiveServer\" />", appMarkup, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PTDoc.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}

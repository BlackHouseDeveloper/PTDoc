using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.RegularExpressions;
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
    public void SignUpGuidance_IsExposedThroughAssociatedHelperText()
    {
        var repoRoot = FindRepoRoot();
        var loginMarkup = File.ReadAllText(Path.Combine(repoRoot, "src/PTDoc.UI/Pages/Login.razor"));

        AssertFieldHelp(loginMarkup, "fullName", "fullName-help", "Enter your full legal name");
        AssertFieldHelp(loginMarkup, "dateOfBirth", "dateOfBirth-help", "You must be at least 18 years old");
        AssertFieldHelp(loginMarkup, "roleKey", "roleKey-help", "Select your role for clinic onboarding");
        AssertFieldHelp(loginMarkup, "licenseNumber", "licenseNumber-help", "Enter your PT/PTA license number");
        AssertFieldHelp(loginMarkup, "licenseState", "licenseState-help", "Select the U.S. state");

        Assert.DoesNotContain("title=\"Enter your full legal name", loginMarkup, StringComparison.Ordinal);
        Assert.DoesNotContain("title=\"You must be at least 18", loginMarkup, StringComparison.Ordinal);
        Assert.DoesNotContain("title=\"Select your role", loginMarkup, StringComparison.Ordinal);
        Assert.DoesNotContain("title=\"Enter your PT/PTA license", loginMarkup, StringComparison.Ordinal);
        Assert.DoesNotContain("title=\"Select the U.S. state", loginMarkup, StringComparison.Ordinal);
    }

    [Fact]
    public void WebHeadOutlet_IsInteractive_ForClientSideTitleUpdates()
    {
        var repoRoot = FindRepoRoot();
        var appMarkup = File.ReadAllText(Path.Combine(repoRoot, "src/PTDoc.Web/Components/App.razor"));
        var headOutlet = Regex.Match(
            appMarkup,
            @"<\s*HeadOutlet\b(?<attributes>[^>]*)>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        Assert.True(headOutlet.Success, "App.razor should render a HeadOutlet component.");
        var attributes = headOutlet.Groups["attributes"].Value;
        Assert.Contains("@rendermode", attributes, StringComparison.Ordinal);
        Assert.Contains("InteractiveServer", attributes, StringComparison.Ordinal);
    }

    private static void AssertFieldHelp(
        string markup,
        string fieldId,
        string helpId,
        string expectedHelpText)
    {
        Assert.Contains($"id=\"{fieldId}\"", markup, StringComparison.Ordinal);
        Assert.Contains($"aria-describedby=\"{helpId}\"", markup, StringComparison.Ordinal);
        Assert.Contains($"id=\"{helpId}\"", markup, StringComparison.Ordinal);
        Assert.Contains(expectedHelpText, markup, StringComparison.Ordinal);
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

using System.ComponentModel.DataAnnotations;
using System.Reflection;
using PTDoc.UI.Pages;

namespace PTDoc.Tests.UI.Pages;

[Trait("Category", "CoreCi")]
public sealed class LoginBaseValidationTests
{
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
}

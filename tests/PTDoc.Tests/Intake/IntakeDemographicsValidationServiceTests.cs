using PTDoc.Infrastructure.Services;

namespace PTDoc.Tests.Intake;

[Trait("Category", "CoreCi")]
public sealed class IntakeDemographicsValidationServiceTests
{
    [Theory]
    [InlineData("Audit Test 085043")]
    [InlineData("Audit Test")]
    [InlineData("Anne-Marie O'Neil 2")]
    public void Validate_AcceptsNamesWithAuditSuffixes(string fullName)
    {
        var service = new IntakeDemographicsValidationService();

        var result = service.Validate(
            fullName,
            DateTime.UtcNow.Date.AddYears(-30),
            "audit@example.com",
            "555-010-0200",
            null,
            null);

        Assert.True(result.IsValid, result.SummaryMessage);
    }

    [Fact]
    public void Validate_StillRequiresFirstAndLastName()
    {
        var service = new IntakeDemographicsValidationService();

        var result = service.Validate(
            "Audit 085043",
            DateTime.UtcNow.Date.AddYears(-30),
            "audit@example.com",
            "555-010-0200",
            null,
            null);

        Assert.False(result.IsValid);
        Assert.Equal("Enter at least first and last name.", result.FieldErrors["FullName"]);
    }
}

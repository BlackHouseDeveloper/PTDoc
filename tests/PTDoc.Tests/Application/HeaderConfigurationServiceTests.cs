using PTDoc.Application.Configurations.Header;

namespace PTDoc.Tests.Application;

[Trait("Category", "CoreCi")]
public sealed class HeaderConfigurationServiceTests
{
    private readonly HeaderConfigurationService _service = new();

    [Theory]
    [InlineData("/intake?step=0", "Step 1 of 4: Demographics")]
    [InlineData("/intake?step=1", "Step 2 of 4: Medical History / Pain Assessment")]
    [InlineData("/intake?step=2", "Step 3 of 4: Pain Details")]
    [InlineData("/intake?step=3", "Step 4 of 4: Review")]
    [InlineData("/intake?step=4", "Step 4 of 4: Review")] // 1-based query values are supported
    [InlineData("/intake?step=PainAssessment", "Step 2 of 4: Medical History / Pain Assessment")]
    [InlineData("/intake/123?step=Review", "Step 4 of 4: Review")]
    public void GetConfiguration_ReturnsStepSubtitle_ForIntakeRoutes(string route, string expectedSubtitle)
    {
        var configuration = _service.GetConfiguration(route);

        Assert.Equal(expectedSubtitle, configuration.Subtitle);
    }

    [Theory]
    [InlineData("/intake")]
    [InlineData("/intake?mode=patient")]
    [InlineData("/intake?step=")]
    [InlineData("/intake?step=unknown")]
    [InlineData("/intake/123")]
    [InlineData("/intake/123?mode=patient")]
    public void GetConfiguration_LeavesSubtitleUnset_WhenStepIsMissingOrInvalid(string route)
    {
        var configuration = _service.GetConfiguration(route);

        Assert.Null(configuration.Subtitle);
    }
}

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using PTDoc.AI.Services;
using PTDoc.Application.AI;
using Xunit;

namespace PTDoc.Tests.AI;

public class AiServiceTests
{
    private readonly IAiService _aiService;

    public AiServiceTests()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Ai:Model", "gpt-4" }
            })
            .Build();

        _aiService = new OpenAiService(configuration, NullLogger<OpenAiService>.Instance);
    }

    [Fact]
    public async Task GenerateAssessment_WithValidRequest_ReturnsSuccess()
    {
        // Arrange
        var request = new AiAssessmentRequest
        {
            ChiefComplaint = "Lower back pain",
            CurrentSymptoms = "Pain radiating down left leg",
            ExaminationFindings = "Limited ROM in lumbar flexion"
        };

        // Act
        var result = await _aiService.GenerateAssessmentAsync(request);

        // Assert
        Assert.True(result.Success);
        Assert.NotEmpty(result.GeneratedText);
        Assert.Equal("v1", result.Metadata.TemplateVersion);
        Assert.Equal("gpt-4", result.Metadata.Model);
        Assert.True(result.Metadata.TokenCount > 0);
    }

    [Fact]
    public async Task GeneratePlan_WithValidRequest_ReturnsSuccess()
    {
        // Arrange
        var request = new AiPlanRequest
        {
            Diagnosis = "Lumbar strain",
            AssessmentSummary = "Patient demonstrates limited ROM and functional deficits",
            Goals = "Return to normal daily activities"
        };

        // Act
        var result = await _aiService.GeneratePlanAsync(request);

        // Assert
        Assert.True(result.Success);
        Assert.NotEmpty(result.GeneratedText);
        Assert.Equal("v1", result.Metadata.TemplateVersion);
        Assert.Equal("gpt-4", result.Metadata.Model);
    }

    [Fact]
    public async Task GenerateAssessment_WithMinimalRequest_ReturnsSuccess()
    {
        // Arrange
        var request = new AiAssessmentRequest
        {
            ChiefComplaint = "Knee pain"
        };

        // Act
        var result = await _aiService.GenerateAssessmentAsync(request);

        // Assert
        Assert.True(result.Success);
        Assert.NotEmpty(result.GeneratedText);
    }

    [Fact]
    public async Task GeneratePlan_WithMinimalRequest_ReturnsSuccess()
    {
        // Arrange
        var request = new AiPlanRequest
        {
            Diagnosis = "Patellofemoral pain syndrome"
        };

        // Act
        var result = await _aiService.GeneratePlanAsync(request);

        // Assert
        Assert.True(result.Success);
        Assert.NotEmpty(result.GeneratedText);
    }

    [Fact]
    public async Task GenerateAssessment_MetadataContainsNoPhiFields()
    {
        // Arrange
        var request = new AiAssessmentRequest
        {
            ChiefComplaint = "Shoulder pain",
            PatientHistory = "Previous rotator cuff surgery"
        };

        // Act
        var result = await _aiService.GenerateAssessmentAsync(request);

        // Assert - metadata should only contain safe fields
        Assert.NotNull(result.Metadata);
        Assert.NotEmpty(result.Metadata.TemplateVersion);
        Assert.NotEmpty(result.Metadata.Model);
        Assert.NotEqual(default(DateTime), result.Metadata.GeneratedAtUtc);
        // NO patient name, MRN, or clinical details in metadata
    }

    [Fact]
    public async Task GeneratePlan_MetadataContainsNoPhiFields()
    {
        // Arrange
        var request = new AiPlanRequest
        {
            Diagnosis = "Rotator cuff tear",
            Precautions = "Post-surgical protocol"
        };

        // Act
        var result = await _aiService.GeneratePlanAsync(request);

        // Assert - metadata should only contain safe fields
        Assert.NotNull(result.Metadata);
        Assert.NotEmpty(result.Metadata.TemplateVersion);
        Assert.NotEmpty(result.Metadata.Model);
        Assert.NotEqual(default(DateTime), result.Metadata.GeneratedAtUtc);
        // NO patient name, MRN, or clinical details in metadata
    }

    [Fact]
    public async Task GenerateAssessment_NullRequest_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => 
            _aiService.GenerateAssessmentAsync(null!));
    }

    [Fact]
    public async Task GeneratePlan_NullRequest_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => 
            _aiService.GeneratePlanAsync(null!));
    }
}

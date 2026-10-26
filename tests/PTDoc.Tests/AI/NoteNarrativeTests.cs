using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PTDoc.AI.Services;
using PTDoc.Application.AI;
using PTDoc.Application.Notes.Workspace;
using PTDoc.UI.Services;
using PTDoc.Core.Models;

namespace PTDoc.Tests.AI;

[Trait("Category", "CoreCi")]
public sealed class NoteNarrativeTests
{
    [Fact]
    public void Context_DoesNotPromoteDefaultsSuggestionsOrPlansIntoMeasuredFacts()
    {
        var p = new NoteWorkspaceV2Payload();
        p.Assessment.GoalSuggestions.Add(new() { Description = "UNACCEPTED GOAL" });
        p.Assessment.Goals.Add(new() { Description = "Accepted goal" });
        p.Assessment.PatientPersonalGoals = "Walk with my dog";
        p.Objective.Metrics.Add(new() { Name = "Flexion", Value = "", NormValue = "180", IsWithinNormalLimits = true });
        p.Objective.ExerciseRows.Add(new() { SuggestedExercise = "SUGGESTED EXERCISE" });
        p.Objective.ExerciseRows.Add(new() { ActualExercisePerformed = "Performed heel raise" });
        p.Plan.GeneralInterventions.Add(new() { Name = "PLANNED THERAPY" });
        p.Plan.ClinicalSummary = "OLD SUMMARY"; p.Plan.FullDischargeSummary = "OLD DISCHARGE SUMMARY";
        var s = NoteNarrativeContextBuilder.Build(p, NoteNarrativePurpose.SubjectiveSummary);
        Assert.Empty(s);
        p.Subjective.IsPainScoreDocumented = true;
        Assert.Equal("0", Assert.Single(NoteNarrativeContextBuilder.Build(p, NoteNarrativePurpose.SubjectiveSummary)).Value);
        var o = NoteNarrativeContextBuilder.Build(p, NoteNarrativePurpose.ObjectiveSummary);
        Assert.Equal("Performed heel raise", Assert.Single(o).Value);
        var a = NoteNarrativeContextBuilder.Build(p, NoteNarrativePurpose.AssessmentSummary);
        Assert.DoesNotContain(a, i => i.Value == "UNACCEPTED GOAL");
        Assert.Contains(a, i => i.Value == "Accepted goal");
        Assert.Contains(a, i => i.Value == "Walk with my dog" && i.Label.Contains("Patient-reported"));
        var plan = NoteNarrativeContextBuilder.Build(p, NoteNarrativePurpose.PlanSummary);
        Assert.Equal("PLANNED THERAPY", Assert.Single(plan).Value);
        Assert.Contains("not evidence of completed", plan[0].Label);
    }

    [Fact]
    public async Task Provider_UsesSectionPromptAndReportsMissingSourcesWithoutCallingTransport()
    {
        var handler = new CaptureHandler();
        var factory = new Mock<IHttpClientFactory>(); factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient(handler));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FeatureFlags:EnableAiGeneration"] = "true", ["AzureOpenAIEndpoint"] = "https://example.openai.azure.com/",
            ["AzureOpenAIKey"] = "synthetic-test-key", ["AzureOpenAIDeployment"] = "synthetic-test-deployment"
        }).Build();
        var provider = new OpenAiService(config, NullLogger<OpenAiService>.Instance, factory.Object);
        var blank = await provider.GenerateSectionAsync(NoteNarrativePurpose.ObjectiveSummary, []);
        Assert.True(blank.Success); Assert.Contains("No objective source facts documented", blank.GeneratedText); Assert.Null(handler.Body);
        var result = await provider.GenerateSectionAsync(NoteNarrativePurpose.SubjectiveSummary, [new("Patient-reported", "Knee pain")]);
        Assert.True(result.Success); Assert.Equal("provider draft", result.GeneratedText);
        Assert.Equal("section-narrative-v1", result.Metadata.TemplateVersion);
        Assert.Contains("Subjective paragraph", handler.Body); Assert.Contains("Do not invent measurements", handler.Body);
        Assert.Contains("Knee pain", handler.Body);
    }

    [Theory]
    [InlineData("ai_feature_disabled", "disabled")]
    [InlineData("ai_source_changed", "note changed")]
    [InlineData("ai_rate_limited", "Too many")]
    [InlineData("arbitrary", "editing manually")]
    public async Task Client_DoesNotExposeProviderErrorText(string code, string expected)
    {
        var handler = new CaptureHandler { Status = HttpStatusCode.BadGateway, Response = JsonSerializer.Serialize(new { code, error = "PRIVATE" }) };
        var client = new HttpNoteNarrativeClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });
        var request = new NoteNarrativeRequest(Guid.NewGuid(), DateTime.UtcNow, NoteNarrativePurpose.SoapSummary);
        var result = await client.GenerateAsync(request);
        Assert.False(result.Success); Assert.Contains(expected, result.ErrorMessage); Assert.DoesNotContain("PRIVATE", result.ErrorMessage);
        using var json = JsonDocument.Parse(handler.Body!); Assert.Equal(3, json.RootElement.EnumerateObject().Count());
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public string? Body { get; private set; }
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        public string Response { get; init; } = "{\"choices\":[{\"message\":{\"content\":\"provider draft\"}}]}";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Body = await request.Content!.ReadAsStringAsync(ct);
            return new(Status) { Content = new StringContent(Response, Encoding.UTF8, "application/json") };
        }
    }
}

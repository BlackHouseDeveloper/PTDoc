namespace PTDoc.Application.AI;

/// <summary>
/// Cost-control rate limits for AI generation endpoints.
/// </summary>
public sealed class AiGenerationRateLimitOptions
{
    public const string SectionName = "Ai:RateLimits";

    public int RequestsPerHour { get; set; } = 10;

    public int WindowMinutes { get; set; } = 60;
}

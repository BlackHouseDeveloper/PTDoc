namespace PTDoc.Application.AI;

/// <summary>
/// Cost-control rate limits for AI generation endpoints.
/// </summary>
public sealed class AiGenerationRateLimitOptions
{
    public const string SectionName = "Ai:RateLimits";

    private int _permitLimit = 10;

    /// <summary>
    /// Number of AI generation requests allowed in the configured fixed window.
    /// </summary>
    public int PermitLimit
    {
        get => _permitLimit;
        set => _permitLimit = value;
    }

    /// <summary>
    /// Compatibility alias for the existing Ai:RateLimits:RequestsPerHour key.
    /// </summary>
    [Obsolete("Use PermitLimit. RequestsPerHour remains for configuration compatibility with Ai:RateLimits:RequestsPerHour.")]
    public int RequestsPerHour
    {
        get => _permitLimit;
        set => _permitLimit = value;
    }

    public int WindowMinutes { get; set; } = 60;
}

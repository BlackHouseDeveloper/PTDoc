namespace PTDoc.Application.AI;

/// <summary>
/// AI text generation service for clinical documentation assistance.
/// STATELESS - does not persist generated content.
/// </summary>
public interface IAiService
{
    /// <summary>
    /// Generates assessment text based on patient data.
    /// </summary>
    /// <param name="request">Structured assessment request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>AI-generated assessment with metadata</returns>
    Task<AiResult> GenerateAssessmentAsync(AiAssessmentRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates plan of care text based on assessment.
    /// </summary>
    /// <param name="request">Structured plan request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>AI-generated plan with metadata</returns>
    Task<AiResult> GeneratePlanAsync(AiPlanRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Request for AI-generated assessment.
/// </summary>
public sealed record AiAssessmentRequest
{
    /// <summary>
    /// Patient's chief complaint or reason for visit.
    /// </summary>
    public required string ChiefComplaint { get; init; }

    /// <summary>
    /// Relevant patient history.
    /// </summary>
    public string? PatientHistory { get; init; }

    /// <summary>
    /// Current symptoms or functional limitations.
    /// </summary>
    public string? CurrentSymptoms { get; init; }

    /// <summary>
    /// Prior level of function.
    /// </summary>
    public string? PriorLevelOfFunction { get; init; }

    /// <summary>
    /// Examination findings.
    /// </summary>
    public string? ExaminationFindings { get; init; }
}

/// <summary>
/// Request for AI-generated plan of care.
/// </summary>
public sealed record AiPlanRequest
{
    /// <summary>
    /// Patient's diagnosis or condition.
    /// </summary>
    public required string Diagnosis { get; init; }

    /// <summary>
    /// Assessment summary from clinician.
    /// </summary>
    public string? AssessmentSummary { get; init; }

    /// <summary>
    /// Functional goals.
    /// </summary>
    public string? Goals { get; init; }

    /// <summary>
    /// Precautions or contraindications.
    /// </summary>
    public string? Precautions { get; init; }
}

/// <summary>
/// Result from AI text generation.
/// </summary>
public sealed record AiResult
{
    /// <summary>
    /// Generated text content.
    /// </summary>
    public required string GeneratedText { get; init; }

    /// <summary>
    /// Metadata about the generation.
    /// </summary>
    public required AiPromptMetadata Metadata { get; init; }

    /// <summary>
    /// Whether generation was successful.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Error message if generation failed.
    /// </summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Metadata about AI prompt and generation.
/// NO PHI - safe for audit logging.
/// </summary>
public sealed record AiPromptMetadata
{
    /// <summary>
    /// Prompt template version used (e.g., "v1").
    /// </summary>
    public required string TemplateVersion { get; init; }

    /// <summary>
    /// AI model identifier (e.g., "gpt-4", "gpt-3.5-turbo").
    /// </summary>
    public required string Model { get; init; }

    /// <summary>
    /// When generation occurred (UTC).
    /// </summary>
    public required DateTime GeneratedAtUtc { get; init; }

    /// <summary>
    /// Token count (if available).
    /// </summary>
    public int? TokenCount { get; init; }
}

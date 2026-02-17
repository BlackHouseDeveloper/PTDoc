namespace PTDoc.Application.AI;

/// <summary>
/// Interface for AI-assisted documentation generation.
/// IMPORTANT: AI services must be stateless and cannot write directly to the database.
/// All persistence happens via explicit clinician save/update flows.
/// </summary>
public interface IAIService
{
    /// <summary>
    /// Generates an assessment narrative from structured patient data.
    /// </summary>
    Task<AIGenerationResult> GenerateAssessmentAsync(AssessmentGenerationRequest request, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Generates a plan narrative from patient data and goals.
    /// </summary>
    Task<AIGenerationResult> GeneratePlanAsync(PlanGenerationRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Request for AI assessment generation.
/// </summary>
public class AssessmentGenerationRequest
{
    public Guid PatientId { get; set; }
    public string? SubjectiveNarrative { get; set; }
    public Dictionary<string, object>? ObjectiveMeasurements { get; set; }
    public string? ChiefComplaint { get; set; }
    public string? Diagnoses { get; set; }
    public string? FunctionalLimitations { get; set; }
}

/// <summary>
/// Request for AI plan generation.
/// </summary>
public class PlanGenerationRequest
{
    public Guid PatientId { get; set; }
    public string? Assessment { get; set; }
    public List<string>? Goals { get; set; }
    public string? Precautions { get; set; }
    public int? FrequencyPerWeek { get; set; }
    public int? DurationWeeks { get; set; }
}

/// <summary>
/// Result of AI generation.
/// </summary>
public class AIGenerationResult
{
    public bool IsSuccessful { get; set; }
    public string? GeneratedText { get; set; }
    public string? ErrorMessage { get; set; }
    
    /// <summary>
    /// Prompt template version used for generation (for audit trail).
    /// </summary>
    public string? PromptTemplateVersion { get; set; }
    
    /// <summary>
    /// AI model identifier (for audit trail).
    /// </summary>
    public string? ModelIdentifier { get; set; }
    
    /// <summary>
    /// Timestamp of generation.
    /// </summary>
    public DateTime GeneratedAtUtc { get; set; }
    
    /// <summary>
    /// Structured segments (optional).
    /// </summary>
    public Dictionary<string, string>? StructuredSegments { get; set; }
}

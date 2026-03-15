namespace PTDoc.Application.AI;

/// <summary>
/// AI clinical narrative generation service for the SOAP note workspace.
/// Supports the generate → review → accept workflow.
/// STATELESS: Does not persist generated content.
/// Safety: Generation is only permitted on draft (unsigned) notes.
/// </summary>
public interface IAiClinicalGenerationService
{
    /// <summary>
    /// Generates an assessment narrative from structured clinical inputs.
    /// </summary>
    Task<AssessmentGenerationResult> GenerateAssessmentAsync(
        AssessmentGenerationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a plan of care narrative from structured clinical inputs.
    /// </summary>
    Task<PlanGenerationResult> GeneratePlanOfCareAsync(
        PlanOfCareGenerationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates goal narratives from structured functional limitation inputs.
    /// </summary>
    Task<GoalGenerationResult> GenerateGoalNarrativesAsync(
        GoalNarrativesGenerationRequest request,
        CancellationToken cancellationToken = default);
}

// ──────────────────────────────────────────────────────────────────
// Request models
// ──────────────────────────────────────────────────────────────────

/// <summary>
/// Request for AI-generated assessment narrative.
/// </summary>
public sealed record AssessmentGenerationRequest
{
    /// <summary>
    /// Identity of the note being authored (used for safety audit only, not passed to AI prompt).
    /// </summary>
    public required Guid NoteId { get; init; }

    /// <summary>
    /// Patient's chief complaint or reason for visit.
    /// </summary>
    public required string ChiefComplaint { get; init; }

    /// <summary>
    /// Relevant clinical history.
    /// </summary>
    public string? PatientHistory { get; init; }

    /// <summary>
    /// Current symptoms or functional limitations.
    /// </summary>
    public string? CurrentSymptoms { get; init; }

    /// <summary>
    /// Prior level of function before injury or illness.
    /// </summary>
    public string? PriorLevelOfFunction { get; init; }

    /// <summary>
    /// Objective examination findings (ROM, MMT, etc.).
    /// </summary>
    public string? ExaminationFindings { get; init; }

    /// <summary>
    /// Identified functional limitations.
    /// </summary>
    public string? FunctionalLimitations { get; init; }

    /// <summary>
    /// Safety guard: generation is rejected when true.
    /// The caller (API or UI) is responsible for setting this from the note's signature state.
    /// </summary>
    public bool IsNoteSigned { get; init; } = false;
}

/// <summary>
/// Request for AI-generated plan of care narrative.
/// </summary>
public sealed record PlanOfCareGenerationRequest
{
    /// <summary>
    /// Identity of the note being authored (used for safety audit only, not passed to AI prompt).
    /// </summary>
    public required Guid NoteId { get; init; }

    /// <summary>
    /// Clinical diagnosis or condition.
    /// </summary>
    public required string Diagnosis { get; init; }

    /// <summary>
    /// Assessment summary from the clinician.
    /// </summary>
    public string? AssessmentSummary { get; init; }

    /// <summary>
    /// Functional goals for the patient.
    /// </summary>
    public string? Goals { get; init; }

    /// <summary>
    /// Precautions or contraindications.
    /// </summary>
    public string? Precautions { get; init; }

    /// <summary>
    /// Safety guard: generation is rejected when true.
    /// </summary>
    public bool IsNoteSigned { get; init; } = false;
}

/// <summary>
/// Request for AI-generated goal narratives.
/// </summary>
public sealed record GoalNarrativesGenerationRequest
{
    /// <summary>
    /// Identity of the note being authored (used for safety audit only, not passed to AI prompt).
    /// </summary>
    public required Guid NoteId { get; init; }

    /// <summary>
    /// Clinical diagnosis or condition.
    /// </summary>
    public required string Diagnosis { get; init; }

    /// <summary>
    /// Identified functional limitations requiring goal coverage.
    /// </summary>
    public required string FunctionalLimitations { get; init; }

    /// <summary>
    /// Prior level of function before injury or illness.
    /// </summary>
    public string? PriorLevelOfFunction { get; init; }

    /// <summary>
    /// Existing short-term goals (used to inform generation, not overwritten).
    /// </summary>
    public string? ShortTermGoals { get; init; }

    /// <summary>
    /// Existing long-term goals (used to inform generation, not overwritten).
    /// </summary>
    public string? LongTermGoals { get; init; }

    /// <summary>
    /// Outcome measure context to inform goal generation with quantitative targets.
    /// Optional: when provided, goals will reference the measure and improvement target.
    /// </summary>
    public OutcomeContext? OutcomeContext { get; init; }

    /// <summary>
    /// Safety guard: generation is rejected when true.
    /// </summary>
    public bool IsNoteSigned { get; init; } = false;
}

/// <summary>
/// Outcome measure data used to inform AI goal generation.
/// No PHI — contains only measurement metadata and scores.
/// </summary>
public sealed record OutcomeContext
{
    /// <summary>
    /// The name of the outcome measure instrument (e.g. "LEFS", "ODI").
    /// </summary>
    public required string MeasureName { get; init; }

    /// <summary>
    /// The patient's baseline (initial) score.
    /// </summary>
    public required double BaselineScore { get; init; }

    /// <summary>
    /// The patient's most recent score.
    /// </summary>
    public required double CurrentScore { get; init; }

    /// <summary>
    /// The maximum possible score for the instrument.
    /// </summary>
    public required double MaxScore { get; init; }

    /// <summary>
    /// Whether a higher score is better (true for LEFS, false for ODI/DASH/NDI).
    /// </summary>
    public required bool HigherIsBetter { get; init; }

    /// <summary>
    /// The minimum clinically important difference for this measure.
    /// </summary>
    public double MinimumClinicallyImportantDifference { get; init; }

    /// <summary>
    /// Human-readable interpretation of the current score (e.g. "Moderate disability").
    /// </summary>
    public string? CurrentInterpretation { get; init; }
}

// ──────────────────────────────────────────────────────────────────
// Result models
// ──────────────────────────────────────────────────────────────────

/// <summary>
/// Result from AI-generated assessment narrative.
/// </summary>
public sealed record AssessmentGenerationResult
{
    /// <summary>AI-generated narrative text, empty on failure.</summary>
    public required string GeneratedText { get; init; }

    /// <summary>Confidence score 0.0–1.0 (mock: always 0.85).</summary>
    public required double Confidence { get; init; }

    /// <summary>Non-blocking warnings (e.g. missing optional inputs).</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>The structured inputs that produced this result (for lineage tracking).</summary>
    public required AssessmentGenerationRequest SourceInputs { get; init; }

    /// <summary>Whether generation succeeded.</summary>
    public bool Success { get; init; }

    /// <summary>Error description when Success is false.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Prompt and model metadata (NO PHI).</summary>
    public AiPromptMetadata? Metadata { get; init; }
}

/// <summary>
/// Result from AI-generated plan of care narrative.
/// </summary>
public sealed record PlanGenerationResult
{
    /// <summary>AI-generated narrative text, empty on failure.</summary>
    public required string GeneratedText { get; init; }

    /// <summary>Confidence score 0.0–1.0.</summary>
    public required double Confidence { get; init; }

    /// <summary>Non-blocking warnings.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>The structured inputs that produced this result.</summary>
    public required PlanOfCareGenerationRequest SourceInputs { get; init; }

    /// <summary>Whether generation succeeded.</summary>
    public bool Success { get; init; }

    /// <summary>Error description when Success is false.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Prompt and model metadata (NO PHI).</summary>
    public AiPromptMetadata? Metadata { get; init; }
}

/// <summary>
/// Result from AI-generated goal narratives.
/// </summary>
public sealed record GoalGenerationResult
{
    /// <summary>AI-generated narrative text, empty on failure.</summary>
    public required string GeneratedText { get; init; }

    /// <summary>Confidence score 0.0–1.0.</summary>
    public required double Confidence { get; init; }

    /// <summary>Non-blocking warnings.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>The structured inputs that produced this result.</summary>
    public required GoalNarrativesGenerationRequest SourceInputs { get; init; }

    /// <summary>Whether generation succeeded.</summary>
    public bool Success { get; init; }

    /// <summary>Error description when Success is false.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Prompt and model metadata (NO PHI).</summary>
    public AiPromptMetadata? Metadata { get; init; }
}

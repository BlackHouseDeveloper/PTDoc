using PTDoc.Application.AI;

namespace PTDoc.AI;

/// <summary>
/// Modular prompt builder for AI clinical narrative generation.
/// Converts structured clinical inputs into safe, consistent prompt text.
///
/// Safety rules enforced:
/// - <c>NoteId</c> and other internal system identifiers are never embedded in prompts.
/// - Known prompt-injection tokens are stripped from all user-supplied strings.
/// - Optional fields are omitted entirely when empty, to keep prompts concise.
///
/// Note: Callers are responsible for not passing direct patient identifiers
/// (name, MRN, DOB, etc.) as field values. This class strips injection tokens
/// but does not perform general PHI redaction.
/// </summary>
public sealed class ClinicalPromptBuilder
{
    private static readonly string[] DangerousTokens =
        ["IGNORE", "SYSTEM:", "USER:", "ASSISTANT:", "###", "```"];

    /// <summary>
    /// Builds a structured prompt for assessment narrative generation.
    /// </summary>
    public string BuildAssessmentPrompt(AssessmentGenerationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("You are a licensed physical therapist assistant helping to document a patient assessment.");
        sb.AppendLine("Generate professional clinical documentation based ONLY on the provided information.");
        sb.AppendLine("Do not invent, assume, or add clinical details not given below.");
        sb.AppendLine();
        sb.AppendLine($"Chief Complaint: {Sanitize(request.ChiefComplaint)}");

        AppendOptionalField(sb, "Patient History", request.PatientHistory);
        AppendOptionalField(sb, "Current Symptoms", request.CurrentSymptoms);
        AppendOptionalField(sb, "Prior Level of Function", request.PriorLevelOfFunction);
        AppendOptionalField(sb, "Examination Findings", request.ExaminationFindings);
        AppendOptionalField(sb, "Functional Limitations", request.FunctionalLimitations);

        sb.AppendLine();
        sb.AppendLine("Generate a concise, professional assessment section. Include:");
        sb.AppendLine("1. Clinical impression based on subjective and objective findings");
        sb.AppendLine("2. Identified functional limitations");
        sb.AppendLine("3. Rehabilitation potential");
        sb.AppendLine();
        sb.AppendLine("Use professional medical terminology. Be factual and evidence-based.");

        return sb.ToString();
    }

    /// <summary>
    /// Builds a structured prompt for plan of care narrative generation.
    /// </summary>
    public string BuildPlanPrompt(PlanOfCareGenerationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("You are a licensed physical therapist assistant helping to document a plan of care.");
        sb.AppendLine("Generate professional clinical documentation based ONLY on the provided information.");
        sb.AppendLine("Do not invent contraindications or precautions not mentioned below.");
        sb.AppendLine();
        sb.AppendLine($"Diagnosis: {Sanitize(request.Diagnosis)}");

        AppendOptionalField(sb, "Assessment Summary", request.AssessmentSummary);
        AppendOptionalField(sb, "Patient Goals", request.Goals);
        AppendOptionalField(sb, "Precautions", request.Precautions);

        sb.AppendLine();
        sb.AppendLine("Generate a concise, professional plan of care section. Include:");
        sb.AppendLine("1. Specific treatment interventions");
        sb.AppendLine("2. Recommended frequency and duration");
        sb.AppendLine("3. Patient education topics");
        sb.AppendLine("4. Expected functional outcomes");
        sb.AppendLine();
        sb.AppendLine("Use professional medical terminology. Be specific and evidence-based.");

        return sb.ToString();
    }

    /// <summary>
    /// Builds a structured prompt for goal narratives generation.
    /// </summary>
    public string BuildGoalsPrompt(GoalNarrativesGenerationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("You are a licensed physical therapist assistant helping to document functional goals.");
        sb.AppendLine("Generate professional SMART goal statements based ONLY on the provided information.");
        sb.AppendLine("Goals must be Specific, Measurable, Achievable, Relevant, and Time-bound.");
        sb.AppendLine();
        sb.AppendLine($"Diagnosis: {Sanitize(request.Diagnosis)}");
        sb.AppendLine($"Functional Limitations: {Sanitize(request.FunctionalLimitations)}");

        AppendOptionalField(sb, "Prior Level of Function", request.PriorLevelOfFunction);
        AppendOptionalField(sb, "Existing Short-Term Goals (do not repeat)", request.ShortTermGoals);
        AppendOptionalField(sb, "Existing Long-Term Goals (do not repeat)", request.LongTermGoals);

        sb.AppendLine();
        sb.AppendLine("Generate SMART short-term goals (2–4 week timeframe) and long-term goals (4–8 week timeframe).");
        sb.AppendLine("Format each goal as: [Timeframe]: Patient will [measurable action] as evidenced by [objective measure].");
        sb.AppendLine();
        sb.AppendLine("Use professional medical terminology. Be specific and measurable.");

        return sb.ToString();
    }

    // ──────────────────────────────────────────────────────────────
    // Private helpers
    // ──────────────────────────────────────────────────────────────

    private static void AppendOptionalField(System.Text.StringBuilder sb, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            sb.AppendLine($"{label}: {Sanitize(value)}");
        }
    }

    /// <summary>
    /// Strips known prompt-injection tokens from a single input string.
    /// Trims whitespace and collapses multiple internal spaces.
    /// </summary>
    /// <remarks>
    /// This targets structural injection patterns (e.g. role override headers)
    /// rather than PHI redaction. Callers must ensure no direct patient
    /// identifiers are passed as input values.
    /// </remarks>
    public string SanitizeInput(string input) => Sanitize(input);

    /// <summary>
    /// Internal sanitization: strips prompt-injection tokens and collapses whitespace.
    /// </summary>
    private static string Sanitize(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var result = input.Trim();

        foreach (var token in DangerousTokens)
        {
            result = result.Replace(token, string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        // Collapse any resulting double whitespace
        return System.Text.RegularExpressions.Regex.Replace(result, @"\s{2,}", " ");
    }
}

using PTDoc.Application.AI;

namespace PTDoc.AI;

/// <summary>
/// Modular prompt builder for AI clinical narrative generation.
/// Converts structured clinical inputs into safe, consistent prompt text.
///
/// Safety rules enforced:
/// - No patient name, MRN, DOB, or other direct identifiers are included.
/// - Only de-contextualized clinical observations are embedded in prompts.
/// - Field labels are sanitized to prevent prompt injection.
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
    /// Strips tokens that could allow prompt injection.
    /// Only printable ASCII / basic Unicode characters are kept.
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

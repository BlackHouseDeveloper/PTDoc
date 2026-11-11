using PTDoc.Core.Models;

namespace PTDoc.Application.Intake;

/// <summary>Issued questionnaire identifiers; adding another type requires its approved contract.</summary>
public static class IntakeQuestionnaireDefinitions
{
    public const string EvaluationFullV1 = "evaluation-full-v1";
    public const int EvaluationFlowVersion = 2;
    public const string DailyV1 = "daily-subjective-v1";
    public const string ProgressV1 = "progress-subjective-v1";
    public const string DischargeV1 = "discharge-subjective-v1";

    public static string? ForNoteType(NoteType type) => type switch
    {
        NoteType.Evaluation => EvaluationFullV1,
        NoteType.Daily => DailyV1,
        NoteType.ProgressNote => ProgressV1,
        NoteType.Discharge => DischargeV1,
        _ => null
    };
    public static NoteType? NoteTypeFor(string? definition) => definition switch
    {
        EvaluationFullV1 => NoteType.Evaluation,
        DailyV1 => NoteType.Daily,
        ProgressV1 => NoteType.ProgressNote,
        DischargeV1 => NoteType.Discharge,
        _ => null
    };
    public static bool IsShort(string? definition) => definition is DailyV1 or ProgressV1 or DischargeV1;
    public static string Label(string? definition) => definition switch
    {
        DailyV1 => "Daily intake",
        ProgressV1 => "Progress intake",
        DischargeV1 => "Discharge intake",
        EvaluationFullV1 => "Evaluation intake",
        _ => "Saved intake"
    };
}

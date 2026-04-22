namespace PTDoc.UI.Components.Notes.Workspace;

public sealed record EditableNarrativeAcceptResult(bool Success, string? ErrorMessage = null)
{
    public static EditableNarrativeAcceptResult Ok() => new(true);

    public static EditableNarrativeAcceptResult Fail(string? errorMessage) => new(false, errorMessage);
}

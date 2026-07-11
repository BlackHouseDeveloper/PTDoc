using PTDoc.UI.Components.Notes.Models;

namespace PTDoc.UI.Components.Notes.Completion;

public sealed record NoteSectionCompletionState(
    SoapSection Section,
    int MissingCount,
    bool IsComplete);

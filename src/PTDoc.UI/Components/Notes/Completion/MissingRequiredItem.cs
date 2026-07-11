using PTDoc.UI.Components.Notes.Models;

namespace PTDoc.UI.Components.Notes.Completion;

public sealed record MissingRequiredItem(
    string Key,
    SoapSection Section,
    string Label,
    string Message,
    string FieldSelector,
    string Source = "UI required");

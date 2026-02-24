namespace PTDoc.UI.Components.Notes.Models;

public class NotesFilterState
{
    public string SearchQuery { get; set; } = string.Empty;
    public string NoteType { get; set; } = "all";
    public string Patient { get; set; } = "all";
    public string Status { get; set; } = "all";
    public string DateRange { get; set; } = "all";

    public NotesFilterState Clone()
    {
        return new NotesFilterState
        {
            SearchQuery = SearchQuery,
            NoteType = NoteType,
            Patient = Patient,
            Status = Status,
            DateRange = DateRange
        };
    }
}

namespace PTDoc.UI.Components.Notes.Models;

public class NoteListItemVm
{
    public string Id { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string NoteType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedDate { get; set; }
    public DateTime LastModified { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public IReadOnlyList<string> CptCodes { get; set; } = Array.Empty<string>();
    public string? AttentionReason { get; set; }
    public bool IsUnsigned { get; set; }
    public bool IsPendingCoSign { get; set; }
    public bool IsIncomplete { get; set; }
    public bool HasErrors { get; set; }

    public bool HasGenericAttentionIssue => IsIncomplete || HasErrors;
    public bool NeedsAttention => IsPendingCoSign || HasGenericAttentionIssue;
}

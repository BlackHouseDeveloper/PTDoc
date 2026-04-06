using PTDoc.Core.Models;

namespace PTDoc.UI.Components.ExportCenter;

/// <summary>
/// Enum for export tab types (drives filter schema and context)
/// </summary>
public enum ExportTab
{
    SoapNotes = 0,
    PatientData = 1,
    Appointments = 2,
    Reports = 3
}

/// <summary>
/// Enum for export format options
/// </summary>
public enum ExportFormat
{
    PDF = 0,
    CSV = 1,
    Excel = 2,
    JSON = 3
}

/// <summary>
/// UI-only state model for the export draft (not persisted)
/// Tracks selections made during export configuration before preview/download
/// </summary>
public class ExportDraftState
{
    public ExportTab SelectedTab { get; set; } = ExportTab.SoapNotes;
    
    public ExportFormat SelectedFormat { get; set; } = ExportFormat.PDF;
    
    // Filter state
    public DateTime? DateRangeStart { get; set; }
    public DateTime? DateRangeEnd { get; set; }
    public List<string> SelectedPatientIds { get; set; } = new();
    public List<string> SelectedProviderIds { get; set; } = new();
    
    // SOAP Notes specific filters
    public List<string> SelectedNoteTypes { get; set; } = new();
    
    // Patient Data specific filters
    public List<string> SelectedDataTypes { get; set; } = new();
    
    // Appointments specific filters
    public List<string> SelectedApptStatuses { get; set; } = new();
    
    // Reports specific filters
    public List<string> SelectedReportTypes { get; set; } = new();
    
    // Export options
    public bool IsPasswordProtected { get; set; } = false;
    public string PasswordValue { get; set; } = string.Empty;
    
    // Preview state
    public bool HasGeneratedPreview { get; set; } = false;
    public int? RecordCount { get; set; }
    public bool IsLoadingPreview { get; set; } = false;
    public bool IsDownloading { get; set; } = false;
    
    /// <summary>
    /// Reset all filter selections and options (called on tab switch)
    /// </summary>
    public void ResetForTabSwitch()
    {
        DateRangeStart = null;
        DateRangeEnd = null;
        SelectedPatientIds.Clear();
        SelectedProviderIds.Clear();
        SelectedNoteTypes.Clear();
        SelectedDataTypes.Clear();
        SelectedApptStatuses.Clear();
        SelectedReportTypes.Clear();
        
        SelectedFormat = ExportFormat.PDF;
        IsPasswordProtected = false;
        PasswordValue = string.Empty;
        
        HasGeneratedPreview = false;
        RecordCount = null;
        IsLoadingPreview = false;
        IsDownloading = false;
    }
}

/// <summary>
/// Real export-center filter options sourced from existing backend entities.
/// </summary>
public sealed class ExportCenterSelectableItem
{
    public string Id { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
}

/// <summary>
/// Real recent-activity item built from existing notes and appointments.
/// </summary>
public sealed class ExportCenterActivityItem
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Meta { get; init; } = string.Empty;
    public DateTime TimestampUtc { get; init; }
    public ExportCenterActivityKind Kind { get; init; }
}

public enum ExportCenterActivityKind
{
    Note,
    Appointment
}

/// <summary>
/// Preview target derived from the currently loaded export-center notes and filters.
/// </summary>
public sealed class ExportPreviewTarget
{
    public Guid? NoteId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public string? SelectionNotice { get; init; }
    public string? UnavailableReason { get; init; }
    public NoteStatus? NoteStatus { get; init; }
    public bool IsSigned => NoteStatus == PTDoc.Core.Models.NoteStatus.Signed;
    public bool CanDownloadPdf { get; init; }
    public bool CanPreview => NoteId.HasValue && string.IsNullOrWhiteSpace(UnavailableReason);
}

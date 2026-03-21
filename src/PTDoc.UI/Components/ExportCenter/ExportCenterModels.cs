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
    public List<int> SelectedPatientIds { get; set; } = new();
    public List<int> SelectedProviderIds { get; set; } = new();
    
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
/// DTO for recent activity items (UI-only, from audit log)
/// </summary>
public class ExportActivityItem
{
    public int Id { get; set; }
    public ExportTab ExportType { get; set; }
    public ExportFormat Format { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public int RecordCount { get; set; }
    public DateTime ExportedAt { get; set; }
    public bool IsSuccess { get; set; } = true;
    
    public string DisplayTitle => $"{ExportTypeLabel(ExportType)} · {Format}";
    
    private static string ExportTypeLabel(ExportTab tab) => tab switch
    {
        ExportTab.SoapNotes => "SOAP Notes",
        ExportTab.PatientData => "Patient Data",
        ExportTab.Appointments => "Appointments",
        ExportTab.Reports => "Reports",
        _ => "Unknown"
    };
}

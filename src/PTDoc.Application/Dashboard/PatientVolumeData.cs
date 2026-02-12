namespace PTDoc.Application.Dashboard;

/// <summary>
/// Patient volume statistics for a given time period
/// </summary>
public sealed record PatientVolumeData
{
    public PatientVolumePeriod Period { get; init; }
    public List<DailyVolume> DailyData { get; init; } = new();
    public VolumeSummary Summary { get; init; } = new();
}

/// <summary>
/// Time period for patient volume data
/// </summary>
public enum PatientVolumePeriod
{
    Last7Days,
    Last30Days,
    Last90Days
}

/// <summary>
/// Daily patient volume entry
/// </summary>
public sealed record DailyVolume
{
    public DateOnly Date { get; init; }
    public int PatientCount { get; init; }
}

/// <summary>
/// Summary statistics for patient volume
/// </summary>
public sealed record VolumeSummary
{
    public int Total { get; init; }
    public double AveragePerDay { get; init; }
    public int Peak { get; init; }
    public DateOnly PeakDate { get; init; }
    public TrendDirection Trend { get; init; }
}

/// <summary>
/// Trend direction for patient volume
/// </summary>
public enum TrendDirection
{
    Up,
    Down,
    Stable
}

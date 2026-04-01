namespace PTDoc.UI.Components.ProgressTracking.Models;

public sealed class ProgressTrackingMetricVm
{
    public string Label { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string? Subtitle { get; init; }
    public int? TrendValue { get; init; }
    public string? TrendText { get; init; }
    public string? BadgeText { get; init; }
    public string BadgeVariant { get; init; } = "default";
}

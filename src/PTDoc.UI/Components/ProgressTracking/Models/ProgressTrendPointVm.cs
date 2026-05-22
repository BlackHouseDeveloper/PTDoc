using PTDoc.Core.Models;

namespace PTDoc.UI.Components.ProgressTracking.Models;

public sealed class ProgressTrendPointVm
{
    public string Label { get; init; } = string.Empty;
    public int Value { get; init; }
    public OutcomeMeasureType MeasureType { get; init; }
    public string MeasureLabel { get; init; } = string.Empty;
    public string ScoreDisplay { get; init; } = string.Empty;
    public string Interpretation { get; init; } = string.Empty;
}

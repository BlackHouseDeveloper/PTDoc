namespace PTDoc.UI.Components.ProgressTracking.Models;

public sealed class ProgressTrackingFilterState
{
    public string DateRange { get; set; } = "30days";
    public string ProgramStatus { get; set; } = "all";
    public string TreatmentPhase { get; set; } = "all";

    public bool IsDischargeSelected =>
        string.Equals(TreatmentPhase, "discharge", StringComparison.OrdinalIgnoreCase)
        || string.Equals(ProgramStatus, "discharged", StringComparison.OrdinalIgnoreCase);

    public ProgressTrackingFilterState Clone()
    {
        return new ProgressTrackingFilterState
        {
            DateRange = DateRange,
            ProgramStatus = ProgramStatus,
            TreatmentPhase = TreatmentPhase
        };
    }
}

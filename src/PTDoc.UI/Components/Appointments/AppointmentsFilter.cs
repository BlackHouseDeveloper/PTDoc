namespace PTDoc.UI.Components.Appointments;

/// <summary>
/// DTO representing the current filter selections for appointments.
/// </summary>
public class AppointmentsFilter
{
    public List<string> SelectedAppointmentTypes { get; set; } = new();
    public List<string> SelectedIntakeStatuses { get; set; } = new();
    public List<string> SelectedAppointmentStatuses { get; set; } = new();

    /// <summary>
    /// Checks if any filters are active.
    /// </summary>
    public bool HasActiveFilters => 
        SelectedAppointmentTypes.Any() || 
        SelectedIntakeStatuses.Any() || 
        SelectedAppointmentStatuses.Any();
}

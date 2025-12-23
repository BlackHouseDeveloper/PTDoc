namespace PTDoc.Models;

/// <summary>
/// Represents a patient in the physical therapy system.
/// </summary>
public sealed class Patient : Entity
{
    /// <summary>
    /// Gets or sets the Medical Record Number (MRN) for the patient.
    /// </summary>
    public string? MRN { get; set; }

    /// <summary>
    /// Gets or sets the patient's first name.
    /// </summary>
    public string FirstName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the patient's last name.
    /// </summary>
    public string LastName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the patient's date of birth.
    /// </summary>
    public DateTime? DateOfBirth { get; set; }

    /// <summary>
    /// Gets or sets the patient's biological sex.
    /// </summary>
    public string? Sex { get; set; }

    /// <summary>
    /// Gets or sets the patient's phone number.
    /// </summary>
    public string? PhoneNumber { get; set; }

    /// <summary>
    /// Gets or sets the patient's email address.
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// Gets or sets the patient's address.
    /// </summary>
    public string? Address { get; set; }

    /// <summary>
    /// Gets or sets the patient's city.
    /// </summary>
    public string? City { get; set; }

    /// <summary>
    /// Gets or sets the patient's state.
    /// </summary>
    public string? State { get; set; }

    /// <summary>
    /// Gets or sets the patient's ZIP code.
    /// </summary>
    public string? ZipCode { get; set; }

    /// <summary>
    /// Gets or sets the patient's medications as a comma-separated string.
    /// </summary>
    public string? MedicationsCsv { get; set; }

    /// <summary>
    /// Gets or sets the patient's comorbidities as a comma-separated string.
    /// </summary>
    public string? ComorbiditiesCsv { get; set; }

    /// <summary>
    /// Gets or sets the patient's assistive devices as a comma-separated string.
    /// </summary>
    public string? AssistiveDevicesCsv { get; set; }

    /// <summary>
    /// Gets or sets the patient's living situation description.
    /// </summary>
    public string? LivingSituation { get; set; }

    /// <summary>
    /// Gets or sets the list of SOAP notes associated with this patient.
    /// </summary>
    public List<SOAPNote> SOAPNotes { get; set; } = new();

    /// <summary>
    /// Gets or sets the list of insurances associated with this patient.
    /// </summary>
    public List<Insurance> Insurances { get; set; } = new();
}

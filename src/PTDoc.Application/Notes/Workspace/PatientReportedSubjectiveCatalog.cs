namespace PTDoc.Application.Notes.Workspace;

/// <summary>
/// Patient-reportable Subjective choices shared by intake and clinician documentation.
/// </summary>
public static class PatientReportedSubjectiveCatalog
{
    public static IReadOnlyList<string> PriorFunctionalLevels { get; } =
    [
        "Independent",
        "Independent with assistive device (e.g., walker, cane, grab bars, bed rail)",
        "Required assistance with transfers (e.g., sit-to-stand, sit-to-supine, supine-to-sit)",
        "Required verbal cues or supervision",
        "Dependent for mobility or self-care"
    ];

    public static IReadOnlyList<string> ImagingModalities { get; } =
    [
        "X-ray",
        "MRI",
        "CT",
        "Ultrasound",
        "Other"
    ];
}

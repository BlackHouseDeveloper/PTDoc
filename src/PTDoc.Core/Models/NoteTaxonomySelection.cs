namespace PTDoc.Core.Models;

/// <summary>
/// First-class row per treatment taxonomy selection on a clinical note.
/// Mirrors TreatmentTaxonomySelections embedded in ClinicalNote.ContentJson,
/// enabling efficient SQL filtering and reporting by taxonomy category or item
/// without JSON parsing or full-table scans.
/// </summary>
public class NoteTaxonomySelection
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ClinicalNoteId { get; set; }

    /// <summary>Catalog category identifier, e.g. "foot-ankle", "strength-endurance".</summary>
    public string CategoryId { get; set; } = string.Empty;

    /// <summary>Display title resolved from the catalog at save time, e.g. "Foot &amp; Ankle".</summary>
    public string CategoryTitle { get; set; } = string.Empty;

    /// <summary>Numeric value of TreatmentTaxonomyCategoryKind (0=GeneralDomain, 1=BodyRegion, 2=CrossCuttingConcept).</summary>
    public int CategoryKind { get; set; }

    /// <summary>Catalog item identifier, e.g. "talocrural-joint-arthrokinematics".</summary>
    public string ItemId { get; set; } = string.Empty;

    /// <summary>Display label resolved from the catalog at save time.</summary>
    public string ItemLabel { get; set; } = string.Empty;

    // Navigation
    public ClinicalNote? Note { get; set; }
}

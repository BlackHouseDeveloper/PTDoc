using PTDoc.Application.ReferenceData;

namespace PTDoc.Application.Outcomes;

public sealed class OutcomeMeasureCatalogAsset
{
    public string Version { get; set; } = string.Empty;
    public ReferenceDataProvenance Provenance { get; set; } = new();
    public List<OutcomeMeasureDefinitionAsset> Measures { get; set; } = new();
}

public sealed class OutcomeMeasureDefinitionAsset
{
    public string MeasureType { get; set; } = string.Empty;
    public string Abbreviation { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public double MinScore { get; set; }
    public double MaxScore { get; set; }
    public bool HigherIsBetter { get; set; }
    public string ScoreUnit { get; set; } = string.Empty;
    public double MinimumClinicallyImportantDifference { get; set; }
    public bool IsSelectableForNewEntry { get; set; } = true;
    public List<string> RecommendedForBodyParts { get; set; } = new();
    public List<OutcomeMeasureScoringBandAsset> ScoringBands { get; set; } = new();
    public ReferenceDataProvenance? Provenance { get; set; }
}

public sealed class OutcomeMeasureScoringBandAsset
{
    public string Label { get; set; } = string.Empty;
    public double MinScore { get; set; }
    public double MaxScore { get; set; }
}

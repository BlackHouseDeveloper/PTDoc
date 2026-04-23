using PTDoc.Application.ReferenceData;
using Xunit;

namespace PTDoc.Tests.ReferenceData;

[Trait("Category", "CoreCi")]
public sealed class ReferenceDataProvenanceNormalizerTests
{
    [Fact]
    public void NormalizeDocumentPath_PreservesNullAndEmpty()
    {
        Assert.Null(ReferenceDataProvenanceNormalizer.NormalizeDocumentPath(null));
        Assert.Equal(string.Empty, ReferenceDataProvenanceNormalizer.NormalizeDocumentPath("   "));
    }

    [Fact]
    public void NormalizeDocumentPath_PreservesCanonicalClinicReferencePath()
    {
        Assert.Equal(
            "docs/clinicrefdata/Commonly used CPT codes and modifiers.md",
            ReferenceDataProvenanceNormalizer.NormalizeDocumentPath(
                "docs/clinicrefdata/Commonly used CPT codes and modifiers.md"));
    }

    [Fact]
    public void NormalizeDocumentPath_MapsKnownBareClinicReferenceFilename()
    {
        Assert.Equal(
            "docs/clinicrefdata/Commonly used CPT codes and modifiers.md",
            ReferenceDataProvenanceNormalizer.NormalizeDocumentPath(
                " Commonly used CPT codes and modifiers.md "));
    }

    [Fact]
    public void NormalizeDocumentPath_MapsKnownBareMarkdownClinicReferenceFilename()
    {
        Assert.Equal(
            "docs/clinicrefdata/ICD-10 codes.md",
            ReferenceDataProvenanceNormalizer.NormalizeDocumentPath("ICD-10 codes.md"));
    }

    [Fact]
    public void NormalizeDocumentPath_PreservesUnrelatedProvenanceValues()
    {
        Assert.Equal(
            "external/reference.md",
            ReferenceDataProvenanceNormalizer.NormalizeDocumentPath(" external/reference.md "));
        Assert.Equal(
            "Future Source.md",
            ReferenceDataProvenanceNormalizer.NormalizeDocumentPath(" Future Source.md "));
    }
}

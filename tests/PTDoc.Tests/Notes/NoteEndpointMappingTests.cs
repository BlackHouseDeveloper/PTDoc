using PTDoc.Api.Notes;
using PTDoc.Core.Models;

namespace PTDoc.Tests.Notes;

[Trait("Category", "CoreCi")]
public sealed class NoteEndpointMappingTests
{
    [Fact]
    public void ToResponse_PreservesPinnedTemplateVersion()
    {
        var templateVersionId = Guid.NewGuid();
        var note = new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = Guid.NewGuid(),
            NoteType = NoteType.Evaluation,
            TemplateVersionId = templateVersionId,
            ContentJson = "{}",
            CptCodesJson = "[]",
            DateOfService = DateTime.UtcNow,
            CreatedUtc = DateTime.UtcNow,
            LastModifiedUtc = DateTime.UtcNow,
            ObjectiveMetrics = []
        };

        var response = NoteEndpoints.ToResponse(note);

        Assert.Equal(templateVersionId, response.TemplateVersionId);
    }
}

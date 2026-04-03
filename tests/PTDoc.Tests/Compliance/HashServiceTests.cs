using PTDoc.Core.Models;
using PTDoc.Infrastructure.Compliance;
using Xunit;

namespace PTDoc.Tests.Compliance;

[Trait("Category", "Compliance")]
public sealed class HashServiceTests
{
    private readonly HashService _hashService = new();

    [Fact]
    public void GenerateHash_SameContentWithDifferentJsonFormatting_ReturnsSameHash()
    {
        var lastModifiedUtc = new DateTime(2026, 4, 3, 15, 0, 0, DateTimeKind.Utc);
        var dateOfService = new DateTime(2026, 4, 3, 9, 0, 0, DateTimeKind.Utc);

        var note1 = CreateNote(
            contentJson: "{ \"assessment\": \"Patient improving\", \"subjective\": \"Reports less pain\" }",
            cptCodesJson: "[{\"code\":\"97110\",\"units\":2},{\"units\":1,\"code\":\"97112\"}]",
            dateOfService: dateOfService,
            lastModifiedUtc: lastModifiedUtc);

        var note2 = CreateNote(
            contentJson: "{\"subjective\":\"Reports  less pain\",\"assessment\":\"Patient improving\"}",
            cptCodesJson: "[{\"code\":\"97112\",\"units\":1},{\"units\":2,\"code\":\"97110\"}]",
            dateOfService: dateOfService,
            lastModifiedUtc: lastModifiedUtc);

        var hash1 = _hashService.GenerateHash(note1);
        var hash2 = _hashService.GenerateHash(note2);

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void GenerateHash_SameObjectiveMetricsInDifferentOrder_ReturnsSameHash()
    {
        var metricA = new ObjectiveMetric
        {
            BodyPart = BodyPart.Knee,
            MetricType = MetricType.ROM,
            Value = "110",
            Side = "Right",
            Unit = "degrees",
            IsWNL = false
        };

        var metricB = new ObjectiveMetric
        {
            BodyPart = BodyPart.Hip,
            MetricType = MetricType.MMT,
            Value = "4/5",
            Side = "Left",
            Unit = "grade",
            IsWNL = false
        };

        var note1 = CreateNote();
        note1.ObjectiveMetrics.Add(metricA);
        note1.ObjectiveMetrics.Add(metricB);

        var note2 = CreateNote();
        note2.ObjectiveMetrics.Add(metricB);
        note2.ObjectiveMetrics.Add(metricA);

        var hash1 = _hashService.GenerateHash(note1);
        var hash2 = _hashService.GenerateHash(note2);

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void GenerateHash_ContentOrTimestampChange_ReturnsDifferentHash()
    {
        var note = CreateNote();
        var changedContent = CreateNote(contentJson: "{\"assessment\":\"changed\"}");
        var changedTimestamp = CreateNote(lastModifiedUtc: note.LastModifiedUtc.AddMinutes(1));

        var originalHash = _hashService.GenerateHash(note);

        Assert.NotEqual(originalHash, _hashService.GenerateHash(changedContent));
        Assert.NotEqual(originalHash, _hashService.GenerateHash(changedTimestamp));
    }

    [Fact]
    public void GenerateHash_MalformedJson_FallsBackToNormalizedRawString()
    {
        var note1 = CreateNote(contentJson: " { not-json } ");
        var note2 = CreateNote(contentJson: "{    not-json    }");

        var hash1 = _hashService.GenerateHash(note1);
        var hash2 = _hashService.GenerateHash(note2);

        Assert.Equal(hash1, hash2);
    }

    private static ClinicalNote CreateNote(
        string contentJson = "{\"assessment\":\"stable\",\"subjective\":\"Reports less pain\"}",
        string cptCodesJson = "[{\"code\":\"97110\",\"units\":2}]",
        DateTime? dateOfService = null,
        DateTime? lastModifiedUtc = null)
    {
        return new ClinicalNote
        {
            Id = Guid.NewGuid(),
            PatientId = Guid.Parse("00000000-0000-0000-0000-000000000111"),
            NoteType = NoteType.Daily,
            DateOfService = dateOfService ?? new DateTime(2026, 4, 3, 9, 0, 0, DateTimeKind.Utc),
            LastModifiedUtc = lastModifiedUtc ?? new DateTime(2026, 4, 3, 15, 0, 0, DateTimeKind.Utc),
            TherapistNpi = "1234567890",
            TotalTreatmentMinutes = 38,
            ContentJson = contentJson,
            CptCodesJson = cptCodesJson
        };
    }
}

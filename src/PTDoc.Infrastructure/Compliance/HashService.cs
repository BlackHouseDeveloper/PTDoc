using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PTDoc.Application.Compliance;
using PTDoc.Core.Models;

namespace PTDoc.Infrastructure.Compliance;

/// <summary>
/// Produces deterministic SHA-256 hashes for the persisted note document state.
/// </summary>
public sealed class HashService : IHashService
{
    private static readonly Regex CollapseWhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    public string GenerateHash(ClinicalNote note)
    {
        ArgumentNullException.ThrowIfNull(note);

        var canonicalDocument = BuildCanonicalDocument(note);
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(canonicalDocument));
        return Convert.ToHexString(hashBytes);
    }

    private static string BuildCanonicalDocument(ClinicalNote note)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("PatientId", note.PatientId.ToString("D"));
            writer.WriteString("NoteType", note.NoteType.ToString());
            writer.WriteString("DateOfService", NormalizeDateTime(note.DateOfService));
            WriteNormalizedStringProperty(writer, "TherapistNpi", note.TherapistNpi);

            if (note.TotalTreatmentMinutes.HasValue)
            {
                writer.WriteNumber("TotalTreatmentMinutes", note.TotalTreatmentMinutes.Value);
            }
            else
            {
                writer.WriteNull("TotalTreatmentMinutes");
            }

            writer.WritePropertyName("ContentJson");
            WriteCanonicalJsonPayload(writer, note.ContentJson, sortTopLevelArrayItems: false);

            writer.WritePropertyName("CptCodesJson");
            WriteCanonicalJsonPayload(writer, note.CptCodesJson, sortTopLevelArrayItems: true);

            writer.WritePropertyName("ObjectiveMetrics");
            WriteObjectiveMetrics(writer, note.ObjectiveMetrics);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteObjectiveMetrics(Utf8JsonWriter writer, IEnumerable<ObjectiveMetric>? metrics)
    {
        var canonicalMetrics = (metrics ?? Array.Empty<ObjectiveMetric>())
            .Select(BuildCanonicalObjectiveMetricJson)
            .OrderBy(json => json, StringComparer.Ordinal)
            .ToList();

        writer.WriteStartArray();
        foreach (var metricJson in canonicalMetrics)
        {
            using var metricDocument = JsonDocument.Parse(metricJson);
            metricDocument.RootElement.WriteTo(writer);
        }

        writer.WriteEndArray();
    }

    private static string BuildCanonicalObjectiveMetricJson(ObjectiveMetric metric)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("BodyPart", metric.BodyPart.ToString());
            writer.WriteString("MetricType", metric.MetricType.ToString());
            writer.WriteString("Value", NormalizeString(metric.Value));
            WriteNormalizedStringProperty(writer, "Side", metric.Side);
            WriteNormalizedStringProperty(writer, "Unit", metric.Unit);
            writer.WriteBoolean("IsWNL", metric.IsWNL);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteCanonicalJsonPayload(
        Utf8JsonWriter writer,
        string? payload,
        bool sortTopLevelArrayItems)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            writer.WriteStringValue(string.Empty);
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (sortTopLevelArrayItems && document.RootElement.ValueKind == JsonValueKind.Array)
            {
                WriteSortedArray(writer, document.RootElement);
                return;
            }

            WriteCanonicalJsonElement(writer, document.RootElement);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            writer.WriteStringValue(NormalizeString(payload));
        }
    }

    private static void WriteSortedArray(Utf8JsonWriter writer, JsonElement arrayElement)
    {
        var canonicalItems = arrayElement
            .EnumerateArray()
            .Select(CanonicalizeJsonElementToString)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToList();

        writer.WriteStartArray();
        foreach (var item in canonicalItems)
        {
            using var itemDocument = JsonDocument.Parse(item);
            itemDocument.RootElement.WriteTo(writer);
        }

        writer.WriteEndArray();
    }

    private static string CanonicalizeJsonElementToString(JsonElement element)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteCanonicalJsonElement(writer, element);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteCanonicalJsonElement(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalJsonElement(writer, property.Value);
                }

                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonicalJsonElement(writer, item);
                }

                writer.WriteEndArray();
                break;

            case JsonValueKind.String:
                writer.WriteStringValue(NormalizeString(element.GetString()));
                break;

            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static void WriteNormalizedStringProperty(Utf8JsonWriter writer, string propertyName, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(propertyName);
            return;
        }

        writer.WriteString(propertyName, NormalizeString(value));
    }

    private static string NormalizeString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return CollapseWhitespaceRegex.Replace(value.Trim(), " ");
    }

    private static string NormalizeDateTime(DateTime value)
    {
        var utcValue = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

        return utcValue.ToString("O");
    }
}

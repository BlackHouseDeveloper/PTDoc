using System.Text.Json;
using System.Text.Json.Nodes;

namespace PTDoc.Application.Intake;

/// <summary>Source metadata belongs to the server, not to the draft editor.</summary>
public static class IntakeProfileProvenance
{
    public const string StaleRevisionMessage = "Saved intake information changed. Reload the intake before saving or submitting.";
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    private static readonly string[] ProtectedFields =
    [
        "profileSourceRevision",
        "prefilledPrimaryPolicyId", "prefilledSecondaryPolicyId",
        "prefilledPrimaryPolicyModifiedUtc", "prefilledSecondaryPolicyModifiedUtc",
        "prefilledPrimaryLegacyPayerFingerprint", "prefilledSecondaryLegacyPayerFingerprint",
        "profilePrefillSourceModifiedUtc", "prefilledDemographics", "omitPrimaryPolicy", "omitSecondaryPolicy"
    ];

    public static string Preserve(string? storedJson, string? incomingJson)
    {
        var stored = Parse(storedJson);
        var incoming = Parse(incomingJson);
        // Never attach newly accepted server sources to answers from an older editor.
        if (!HasCurrentRevision(stored, incoming))
            throw new InvalidOperationException(StaleRevisionMessage);
        foreach (var name in ProtectedFields)
        {
            var source = stored.FirstOrDefault(p => string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase));
            foreach (var key in incoming.Select(p => p.Key).Where(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase)).ToArray())
                incoming.Remove(key);
            if (source.Key is not null) incoming[name] = source.Value?.DeepClone();
        }
        return incoming.ToJsonString(Options);
    }

    public static bool HasCurrentRevision(JsonObject stored, JsonObject incoming)
    {
        var revision = stored.FirstOrDefault(p => string.Equals(p.Key, "profileSourceRevision", StringComparison.OrdinalIgnoreCase)).Value;
        if (revision is null) return true;
        var supplied = incoming.Where(p => string.Equals(p.Key, "profileSourceRevision", StringComparison.OrdinalIgnoreCase)).ToArray();
        return supplied.Length == 1 && JsonNode.DeepEquals(revision, supplied[0].Value);
    }

    public static JsonObject Parse(string? json)
        => string.IsNullOrWhiteSpace(json) ? new() : JsonNode.Parse(json) as JsonObject
            ?? throw new JsonException("Intake response must be a JSON object.");
}

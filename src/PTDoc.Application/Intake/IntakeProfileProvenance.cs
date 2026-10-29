using System.Text.Json;
using System.Text.Json.Nodes;

namespace PTDoc.Application.Intake;

/// <summary>Source metadata belongs to the server, not to the draft editor.</summary>
public static class IntakeProfileProvenance
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    private static readonly string[] ProtectedFields =
    [
        "prefilledPrimaryPolicyId", "prefilledSecondaryPolicyId",
        "prefilledPrimaryPolicyModifiedUtc", "prefilledSecondaryPolicyModifiedUtc",
        "profilePrefillSourceModifiedUtc", "prefilledDemographics", "omitPrimaryPolicy", "omitSecondaryPolicy"
    ];

    public static string Preserve(string? storedJson, string? incomingJson)
    {
        var stored = Parse(storedJson);
        var incoming = Parse(incomingJson);
        foreach (var name in ProtectedFields)
        {
            var source = stored.FirstOrDefault(p => string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase));
            foreach (var key in incoming.Select(p => p.Key).Where(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase)).ToArray())
                incoming.Remove(key);
            if (source.Key is not null) incoming[name] = source.Value?.DeepClone();
        }
        return incoming.ToJsonString(Options);
    }

    public static JsonObject Parse(string? json)
        => string.IsNullOrWhiteSpace(json) ? new() : JsonNode.Parse(json) as JsonObject
            ?? throw new JsonException("Intake response must be a JSON object.");
}

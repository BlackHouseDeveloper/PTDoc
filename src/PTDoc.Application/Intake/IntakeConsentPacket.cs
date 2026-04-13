using System.Text.Json;
using System.Text.Json.Serialization;

namespace PTDoc.Application.Intake;

/// <summary>
/// Canonical server-side representation of patient consents captured during intake.
/// Additional fields are preserved to avoid dropping future consent data during normalization.
/// </summary>
public sealed class IntakeConsentPacket
{
    [JsonPropertyName("treatmentConsentAccepted")]
    public bool? TreatmentConsentAccepted { get; set; }

    [JsonPropertyName("hipaaAcknowledged")]
    public bool? HipaaAcknowledged { get; set; }

    [JsonPropertyName("phiReleaseAuthorized")]
    public bool? PhiReleaseAuthorized { get; set; }

    [JsonPropertyName("mediaConsentAccepted")]
    public bool? MediaConsentAccepted { get; set; }

    [JsonPropertyName("communicationCallConsent")]
    public bool? CommunicationCallConsent { get; set; }

    [JsonPropertyName("communicationTextConsent")]
    public bool? CommunicationTextConsent { get; set; }

    [JsonPropertyName("communicationEmailConsent")]
    public bool? CommunicationEmailConsent { get; set; }

    [JsonPropertyName("communicationPhoneNumber")]
    public string? CommunicationPhoneNumber { get; set; }

    [JsonPropertyName("communicationEmail")]
    public string? CommunicationEmail { get; set; }

    [JsonPropertyName("dryNeedlingConsentAccepted")]
    public bool? DryNeedlingConsentAccepted { get; set; }

    [JsonPropertyName("pelvicFloorConsentAccepted")]
    public bool? PelvicFloorConsentAccepted { get; set; }

    [JsonPropertyName("creditCardAuthorizationAccepted")]
    public bool? CreditCardAuthorizationAccepted { get; set; }

    [JsonPropertyName("finalAttestationAccepted")]
    public bool? FinalAttestationAccepted { get; set; }

    [JsonPropertyName("writtenRevocationReceived")]
    public bool? WrittenRevocationReceived { get; set; }

    [JsonPropertyName("lastRevocationAtUtc")]
    public DateTime? LastRevocationAtUtc { get; set; }

    [JsonPropertyName("revokedConsentKeys")]
    public List<string> RevokedConsentKeys { get; set; } = new();

    [JsonPropertyName("authorizedContacts")]
    public List<AuthorizedContact> AuthorizedContacts { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalData { get; set; }
}

public sealed class AuthorizedContact
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("phoneNumber")]
    public string? PhoneNumber { get; set; }

    [JsonPropertyName("relationship")]
    public string? Relationship { get; set; }

    [JsonIgnore]
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Name) &&
        string.IsNullOrWhiteSpace(PhoneNumber) &&
        string.IsNullOrWhiteSpace(Relationship);
}

public sealed class IntakeConsentValidationResult
{
    public Dictionary<string, string[]> Errors { get; } = new(StringComparer.Ordinal);

    public bool IsValid => Errors.Count == 0;

    public void AddError(string key, string message)
    {
        if (Errors.TryGetValue(key, out var existing))
        {
            Errors[key] = existing.Concat(new[] { message }).Distinct(StringComparer.Ordinal).ToArray();
            return;
        }

        Errors[key] = new[] { message };
    }
}

public static class IntakeConsentJson
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public static bool TryParse(string? json, out IntakeConsentPacket packet, out string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(json) || string.Equals(json.Trim(), "{}", StringComparison.Ordinal))
        {
            packet = new IntakeConsentPacket();
            errorMessage = null;
            return true;
        }

        try
        {
            packet = JsonSerializer.Deserialize<IntakeConsentPacket>(json, SerializerOptions) ?? new IntakeConsentPacket();
            packet.AuthorizedContacts ??= new List<AuthorizedContact>();
            packet.RevokedConsentKeys ??= new List<string>();
            errorMessage = null;
            return true;
        }
        catch (JsonException)
        {
            packet = new IntakeConsentPacket();
            errorMessage = "Consents data is not valid JSON.";
            return false;
        }
    }

    public static string Serialize(IntakeConsentPacket packet)
    {
        packet.AuthorizedContacts = packet.AuthorizedContacts
            .Where(contact => !contact.IsEmpty)
            .Take(3)
            .ToList();

        packet.RevokedConsentKeys = packet.RevokedConsentKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return JsonSerializer.Serialize(packet, SerializerOptions);
    }

    public static string Normalize(string? json)
    {
        return TryParse(json, out var packet, out _)
            ? Serialize(packet)
            : "{}";
    }

    public static IntakeConsentValidationResult Validate(IntakeConsentPacket packet, bool requireHipaaAcknowledgement = false)
    {
        var result = new IntakeConsentValidationResult();
        var populatedContacts = packet.AuthorizedContacts.Where(contact => !contact.IsEmpty).ToList();

        if (populatedContacts.Count > 3)
        {
            result.AddError("authorizedContacts", "No more than three authorized contacts may be stored.");
        }

        for (var index = 0; index < populatedContacts.Count; index++)
        {
            var contact = populatedContacts[index];
            var keyPrefix = $"authorizedContacts[{index}]";

            if (string.IsNullOrWhiteSpace(contact.Name))
            {
                result.AddError($"{keyPrefix}.name", "Authorized contact name is required when a contact entry is provided.");
            }

            if (string.IsNullOrWhiteSpace(contact.PhoneNumber))
            {
                result.AddError($"{keyPrefix}.phoneNumber", "Authorized contact phone number is required when a contact entry is provided.");
            }

            if (string.IsNullOrWhiteSpace(contact.Relationship))
            {
                result.AddError($"{keyPrefix}.relationship", "Authorized contact relationship is required when a contact entry is provided.");
            }
        }

        if ((packet.CommunicationCallConsent == true || packet.CommunicationTextConsent == true) &&
            string.IsNullOrWhiteSpace(packet.CommunicationPhoneNumber))
        {
            result.AddError("communicationPhoneNumber", "A phone number is required when phone or text reminders are authorized.");
        }

        if (packet.CommunicationEmailConsent == true && string.IsNullOrWhiteSpace(packet.CommunicationEmail))
        {
            result.AddError("communicationEmail", "An email address is required when email reminders are authorized.");
        }

        if (requireHipaaAcknowledgement)
        {
            var (_, missing, revoked) = EvaluateConsentCompleteness(packet);

            if (missing.Contains("hipaaAcknowledged", StringComparer.OrdinalIgnoreCase))
            {
                result.AddError("hipaaAcknowledged", "HIPAA acknowledgement is required before submission.");
            }

            if (missing.Contains("treatmentConsentAccepted", StringComparer.OrdinalIgnoreCase))
            {
                result.AddError("treatmentConsentAccepted", "Treatment consent is required before submission.");
            }

            if (revoked.Contains("hipaaAcknowledged", StringComparer.OrdinalIgnoreCase))
            {
                result.AddError("hipaaAcknowledged", "HIPAA acknowledgement has been revoked and must be re-authorized before submission.");
            }

            if (revoked.Contains("treatmentConsentAccepted", StringComparer.OrdinalIgnoreCase))
            {
                result.AddError("treatmentConsentAccepted", "Treatment consent has been revoked and must be re-authorized before submission.");
            }
        }

        return result;
    }

    public static IntakeConsentValidationResult ApplyWrittenRevocation(
        IntakeConsentPacket packet,
        IEnumerable<string> consentKeys,
        DateTime timestampUtc)
    {
        var result = new IntakeConsentValidationResult();
        var requestedKeys = consentKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (requestedKeys.Count == 0)
        {
            result.AddError("consentKeys", "At least one consent key is required for revocation.");
            return result;
        }

        foreach (var key in requestedKeys)
        {
            if (!TryMapRevocableConsentKey(key, out var canonicalKey))
            {
                result.AddError("consentKeys", $"Unknown consent key '{key}'.");
                continue;
            }

            if (!packet.RevokedConsentKeys.Contains(canonicalKey, StringComparer.OrdinalIgnoreCase))
            {
                packet.RevokedConsentKeys.Add(canonicalKey);
            }
        }

        if (!result.IsValid)
            return result;

        packet.WrittenRevocationReceived = true;
        packet.LastRevocationAtUtc = timestampUtc;
        return result;
    }

    public static IReadOnlyCollection<string> NormalizeConsentKeys(IEnumerable<string> consentKeys)
    {
        return consentKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool TryMapRevocableConsentKey(
        string key,
        out string canonicalKey)
    {
        canonicalKey = key;

        if (string.Equals(key, "treatmentConsentAccepted", StringComparison.OrdinalIgnoreCase))
        {
            canonicalKey = "treatmentConsentAccepted";
            return true;
        }

        if (string.Equals(key, "hipaaAcknowledged", StringComparison.OrdinalIgnoreCase))
        {
            canonicalKey = "hipaaAcknowledged";
            return true;
        }

        if (string.Equals(key, "phiReleaseAuthorized", StringComparison.OrdinalIgnoreCase))
        {
            canonicalKey = "phiReleaseAuthorized";
            return true;
        }

        if (string.Equals(key, "mediaConsentAccepted", StringComparison.OrdinalIgnoreCase))
        {
            canonicalKey = "mediaConsentAccepted";
            return true;
        }

        if (string.Equals(key, "communicationCallConsent", StringComparison.OrdinalIgnoreCase))
        {
            canonicalKey = "communicationCallConsent";
            return true;
        }

        if (string.Equals(key, "communicationTextConsent", StringComparison.OrdinalIgnoreCase))
        {
            canonicalKey = "communicationTextConsent";
            return true;
        }

        if (string.Equals(key, "communicationEmailConsent", StringComparison.OrdinalIgnoreCase))
        {
            canonicalKey = "communicationEmailConsent";
            return true;
        }

        if (string.Equals(key, "dryNeedlingConsentAccepted", StringComparison.OrdinalIgnoreCase))
        {
            canonicalKey = "dryNeedlingConsentAccepted";
            return true;
        }

        if (string.Equals(key, "pelvicFloorConsentAccepted", StringComparison.OrdinalIgnoreCase))
        {
            canonicalKey = "pelvicFloorConsentAccepted";
            return true;
        }

        if (string.Equals(key, "creditCardAuthorizationAccepted", StringComparison.OrdinalIgnoreCase))
        {
            canonicalKey = "creditCardAuthorizationAccepted";
            return true;
        }

        if (string.Equals(key, "finalAttestationAccepted", StringComparison.OrdinalIgnoreCase))
        {
            canonicalKey = "finalAttestationAccepted";
            return true;
        }

        return false;
    }

    public static Dictionary<string, object> CreateAuditSummary(IntakeConsentPacket packet)
    {
        return new Dictionary<string, object>
        {
            ["HipaaAcknowledged"] = packet.HipaaAcknowledged == true,
            ["TreatmentConsentAccepted"] = packet.TreatmentConsentAccepted == true,
            ["PhiReleaseAuthorized"] = packet.PhiReleaseAuthorized == true,
            ["MediaConsentAccepted"] = packet.MediaConsentAccepted == true,
            ["AuthorizedContactCount"] = packet.AuthorizedContacts.Count(contact => !contact.IsEmpty),
            ["CommunicationCallConsent"] = packet.CommunicationCallConsent == true,
            ["CommunicationTextConsent"] = packet.CommunicationTextConsent == true,
            ["CommunicationEmailConsent"] = packet.CommunicationEmailConsent == true,
            ["DryNeedlingConsentAccepted"] = packet.DryNeedlingConsentAccepted == true,
            ["PelvicFloorConsentAccepted"] = packet.PelvicFloorConsentAccepted == true,
            ["CreditCardAuthorizationAccepted"] = packet.CreditCardAuthorizationAccepted == true,
            ["FinalAttestationAccepted"] = packet.FinalAttestationAccepted == true
        };
    }

    public static bool IsCallConsentActive(IntakeConsentPacket packet)
        => packet.CommunicationCallConsent == true && !IsRevoked(packet, "communicationCallConsent");

    public static bool IsTextConsentActive(IntakeConsentPacket packet)
        => packet.CommunicationTextConsent == true && !IsRevoked(packet, "communicationTextConsent");

    public static bool IsEmailConsentActive(IntakeConsentPacket packet)
        => packet.CommunicationEmailConsent == true && !IsRevoked(packet, "communicationEmailConsent");

    public static bool IsDryNeedlingConsentActive(IntakeConsentPacket packet)
        => packet.DryNeedlingConsentAccepted == true && !IsRevoked(packet, "dryNeedlingConsentAccepted");

    public static bool IsPelvicFloorConsentActive(IntakeConsentPacket packet)
        => packet.PelvicFloorConsentAccepted == true && !IsRevoked(packet, "pelvicFloorConsentAccepted");

    public static bool IsPhiReleaseConsentActive(IntakeConsentPacket packet)
        => packet.PhiReleaseAuthorized == true && !IsRevoked(packet, "phiReleaseAuthorized");

    public static bool IsCreditCardAuthorizationConsentActive(IntakeConsentPacket packet)
        => packet.CreditCardAuthorizationAccepted == true && !IsRevoked(packet, "creditCardAuthorizationAccepted");

    /// <summary>
    /// Evaluates which required consents are missing or revoked.
    /// Required consents for treatment: HIPAA acknowledgement + treatment consent accepted.
    /// Returns true when the intake is ready to proceed to treatment scheduling.
    /// </summary>
    public static (bool IsComplete, List<string> Missing, List<string> Revoked) EvaluateConsentCompleteness(IntakeConsentPacket packet)
    {
        var missing = new List<string>();
        var revoked = new List<string>();

        Check("hipaaAcknowledged", packet.HipaaAcknowledged == true, packet, missing, revoked);
        Check("treatmentConsentAccepted", packet.TreatmentConsentAccepted == true, packet, missing, revoked);

        var isComplete = missing.Count == 0 && revoked.Count == 0;
        return (isComplete, missing, revoked);

        static void Check(string key, bool accepted, IntakeConsentPacket p, List<string> m, List<string> r)
        {
            if (!accepted)
                m.Add(key);
            else if (IsRevoked(p, key))
                r.Add(key);
        }
    }

    private static bool IsRevoked(IntakeConsentPacket packet, string canonicalConsentKey)
    {
        return packet.RevokedConsentKeys.Any(k => string.Equals(k, canonicalConsentKey, StringComparison.OrdinalIgnoreCase));
    }
}

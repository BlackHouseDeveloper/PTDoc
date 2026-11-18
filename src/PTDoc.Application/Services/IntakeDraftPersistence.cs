using PTDoc.Application.Intake;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PTDoc.Application.Services;

public static class IntakeDraftPersistence
{
    private static readonly string[] LegacyMirroredConsentProperties =
    [
        "hipaaAcknowledged",
        "consentToTreatAcknowledged",
        "accuracyConfirmed",
        "revokeHipaaPrivacyNotice",
        "revokeTreatmentConsent",
        "revokeMarketingCommunications",
        "revokePhiRelease",
        "allowPhoneCalls",
        "allowTextMessages",
        "allowEmailMessages",
        "dryNeedlingEligible",
        "pelvicFloorTherapyEligible",
        "phiReleaseAuthorized",
        "billingConsentAuthorized"
    ];

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static IntakeResponseDraft CreatePersistenceCopy(IntakeResponseDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var copy = JsonSerializer.Deserialize<IntakeResponseDraft>(
                       JsonSerializer.Serialize(draft, SerializerOptions),
                       SerializerOptions)
                   ?? new IntakeResponseDraft();

        NormalizeCanonicalConsentState(copy);
        NormalizeCanonicalSupplementalSelections(copy);
        return copy;
    }

    public static string SerializePersistenceJson(IntakeResponseDraft draft)
    {
        var copy = CreatePersistenceCopy(draft);
        copy.ProfilePrefill = null; // Offers are refreshed from the authorized profile, not stored as answers.
        var json = JsonSerializer.Serialize(copy, SerializerOptions);
        if (copy.ConsentPacket is null)
        {
            return json;
        }

        if (JsonNode.Parse(json) is not JsonObject root)
        {
            return json;
        }

        foreach (var propertyName in LegacyMirroredConsentProperties)
        {
            root.Remove(propertyName);
        }

        return root.ToJsonString(SerializerOptions);
    }

    public static string NormalizeSubmittedPersistenceJson(
        string? responseJson,
        Guid intakeId,
        Guid patientId,
        DateTime submittedAtUtc,
        DateTime lastModifiedUtc,
        IntakeConsentPacket? recordedConsents = null)
    {
        var draft = DeserializeDraft(responseJson);
        if (recordedConsents?.HasRevocationHistory == true)
        {
            draft.ConsentPacket = recordedConsents;
            HydrateConsentConvenienceFields(draft);
        }
        return SerializeSubmittedPersistenceJson(draft, intakeId, patientId, submittedAtUtc, lastModifiedUtc);
    }

    public static string SerializeSubmittedPersistenceJson(
        IntakeResponseDraft draft,
        Guid intakeId,
        Guid patientId,
        DateTime submittedAtUtc,
        DateTime lastModifiedUtc)
    {
        var copy = CreatePersistenceCopy(draft);
        copy.IntakeId = intakeId;
        copy.PatientId = patientId;
        copy.IsSubmitted = true;
        copy.IsLocked = true;
        copy.SubmittedAt = submittedAtUtc;
        copy.LastModifiedUtc = lastModifiedUtc;
        return SerializePersistenceJson(copy);
    }

    public static IntakeConsentPacket? CloneConsentPacket(IntakeConsentPacket? packet)
    {
        if (packet is null)
        {
            return null;
        }

        var clone = JsonSerializer.Deserialize<IntakeConsentPacket>(
                        JsonSerializer.Serialize(packet, SerializerOptions),
                        SerializerOptions)
                    ?? new IntakeConsentPacket();
        clone.RevokedConsentKeys ??= new List<string>();
        clone.AuthorizedContacts ??= new List<AuthorizedContact>();
        clone.PhiAuthorizedContactIds ??= new List<string>();
        return clone;
    }

    public static IntakeConsentPacket BuildCanonicalConsentPacket(IntakeResponseDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var packet = CloneConsentPacket(draft.ConsentPacket) ?? new IntakeConsentPacket();
        packet.HipaaAcknowledged ??= LegacyAcceptance("hipaaAcknowledged", draft.HipaaAcknowledged);
        packet.TreatmentConsentAccepted ??= LegacyAcceptance("treatmentConsentAccepted", draft.ConsentToTreatAcknowledged);
        packet.PhiReleaseAuthorized ??= LegacyAcceptance("phiReleaseAuthorized", draft.PhiReleaseAuthorized);
        packet.CommunicationCallConsent ??= LegacyAcceptance("communicationCallConsent", draft.AllowPhoneCalls);
        packet.CommunicationTextConsent ??= LegacyAcceptance("communicationTextConsent", draft.AllowTextMessages);
        packet.CommunicationEmailConsent ??= LegacyAcceptance("communicationEmailConsent", draft.AllowEmailMessages);
        packet.DryNeedlingConsentAccepted ??= LegacyAcceptance("dryNeedlingConsentAccepted", draft.DryNeedlingEligible);
        packet.PelvicFloorConsentAccepted ??= LegacyAcceptance("pelvicFloorConsentAccepted", draft.PelvicFloorTherapyEligible);
        packet.CreditCardAuthorizationAccepted ??= LegacyAcceptance("creditCardAuthorizationAccepted", draft.BillingConsentAuthorized);
        packet.FinalAttestationAccepted ??= LegacyAcceptance("finalAttestationAccepted", draft.AccuracyConfirmed);

        if (!string.IsNullOrWhiteSpace(draft.PhoneNumber))
        {
            packet.CommunicationPhoneNumber = draft.PhoneNumber;
        }

        if (!string.IsNullOrWhiteSpace(draft.EmailAddress))
        {
            packet.CommunicationEmail = draft.EmailAddress;
        }

        MergeLegacyRevocation(packet, "hipaaAcknowledged", draft.RevokeHipaaPrivacyNotice);
        MergeLegacyRevocation(packet, "treatmentConsentAccepted", draft.RevokeTreatmentConsent);
        MergeLegacyRevocation(packet, "phiReleaseAuthorized", draft.RevokePhiRelease);
        MergeLegacyRevocation(packet, "communicationCallConsent", draft.RevokeMarketingCommunications);
        MergeLegacyRevocation(packet, "communicationTextConsent", draft.RevokeMarketingCommunications);
        MergeLegacyRevocation(packet, "communicationEmailConsent", draft.RevokeMarketingCommunications);

        return packet;

        // When the recorded acceptance was absent, a legacy default must not invent one.
        bool? LegacyAcceptance(string key, bool value) => IntakeConsentJson.IsRevoked(packet, key) ? null : value;
    }

    public static void HydrateConsentConvenienceFields(IntakeResponseDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (draft.ConsentPacket is null)
        {
            return;
        }

        draft.ConsentPacket = CloneConsentPacket(draft.ConsentPacket) ?? new IntakeConsentPacket();
        draft.HipaaAcknowledged = draft.ConsentPacket.HipaaAcknowledged == true;
        draft.ConsentToTreatAcknowledged = draft.ConsentPacket.TreatmentConsentAccepted == true;
        draft.RevokeHipaaPrivacyNotice = draft.ConsentPacket.RevokedConsentKeys.Contains("hipaaAcknowledged", StringComparer.OrdinalIgnoreCase);
        draft.RevokeTreatmentConsent = draft.ConsentPacket.RevokedConsentKeys.Contains("treatmentConsentAccepted", StringComparer.OrdinalIgnoreCase);
        draft.RevokePhiRelease = draft.ConsentPacket.RevokedConsentKeys.Contains("phiReleaseAuthorized", StringComparer.OrdinalIgnoreCase);
        draft.RevokeMarketingCommunications =
            draft.ConsentPacket.RevokedConsentKeys.Contains("communicationCallConsent", StringComparer.OrdinalIgnoreCase)
            && draft.ConsentPacket.RevokedConsentKeys.Contains("communicationTextConsent", StringComparer.OrdinalIgnoreCase)
            && draft.ConsentPacket.RevokedConsentKeys.Contains("communicationEmailConsent", StringComparer.OrdinalIgnoreCase);
        draft.AllowPhoneCalls = draft.ConsentPacket.CommunicationCallConsent ?? draft.AllowPhoneCalls;
        draft.AllowTextMessages = draft.ConsentPacket.CommunicationTextConsent ?? draft.AllowTextMessages;
        draft.AllowEmailMessages = draft.ConsentPacket.CommunicationEmailConsent ?? draft.AllowEmailMessages;
        draft.DryNeedlingEligible = draft.ConsentPacket.DryNeedlingConsentAccepted ?? draft.DryNeedlingEligible;
        draft.PelvicFloorTherapyEligible = draft.ConsentPacket.PelvicFloorConsentAccepted ?? draft.PelvicFloorTherapyEligible;
        draft.PhiReleaseAuthorized = draft.ConsentPacket.PhiReleaseAuthorized ?? draft.PhiReleaseAuthorized;
        draft.BillingConsentAuthorized = draft.ConsentPacket.CreditCardAuthorizationAccepted ?? draft.BillingConsentAuthorized;
        draft.AccuracyConfirmed = draft.ConsentPacket.FinalAttestationAccepted ?? draft.AccuracyConfirmed;
    }

    public static void HydratePainSeverityDocumentationFlag(IntakeResponseDraft draft, string? sourceJson)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (draft.PainSeverityProvided || !draft.PainSeverityScore.HasValue)
        {
            return;
        }

        if (HasJsonProperty(sourceJson, "painSeverityProvided"))
        {
            return;
        }

        draft.PainSeverityProvided = true;
    }

    public static void NormalizeCanonicalSupplementalSelections(IntakeResponseDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if ((draft.StructuredData?.ComorbidityIds?.Count ?? 0) > 0)
        {
            draft.SelectedComorbidities.Clear();
        }

        if (draft.StructuredData?.NoComorbidities == true)
        {
            draft.StructuredData.ComorbidityIds.Clear();
            draft.SelectedComorbidities.Clear();
            draft.HasOtherMedicalConditions = false;
        }

        if (draft.StructuredData?.NoAssistiveDevices == true)
        {
            draft.StructuredData.AssistiveDeviceIds.Clear();
            draft.SelectedAssistiveDevices.Clear();
            draft.UsesAssistiveDevices = false;
        }

        if (draft.StructuredData?.NoMedications == true)
        {
            draft.StructuredData.MedicationIds.Clear();
            draft.HasCurrentMedications = false;
        }

        if ((draft.StructuredData?.AssistiveDeviceIds?.Count ?? 0) > 0)
        {
            draft.SelectedAssistiveDevices.Clear();
        }

        if ((draft.StructuredData?.LivingSituationIds?.Count ?? 0) > 0)
        {
            draft.SelectedLivingSituations.Clear();
        }

        if ((draft.StructuredData?.HouseLayoutOptionIds?.Count ?? 0) > 0)
        {
            draft.SelectedHouseLayoutOptions.Clear();
        }
    }

    private static bool HasJsonProperty(string? json, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement
                    .EnumerateObject()
                    .Any(property => string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static IntakeResponseDraft DeserializeDraft(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new IntakeResponseDraft();
        }

        try
        {
            return JsonSerializer.Deserialize<IntakeResponseDraft>(json, SerializerOptions)
                   ?? new IntakeResponseDraft();
        }
        catch (JsonException)
        {
            return new IntakeResponseDraft();
        }
    }

    private static void NormalizeCanonicalConsentState(IntakeResponseDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (!HasConsentState(draft))
        {
            return;
        }

        draft.ConsentPacket = BuildCanonicalConsentPacket(draft);
        HydrateConsentConvenienceFields(draft);
    }

    private static bool HasConsentState(IntakeResponseDraft draft)
    {
        return draft.ConsentPacket is not null
               || draft.HipaaAcknowledged
               || draft.ConsentToTreatAcknowledged
               || draft.AccuracyConfirmed
               || draft.RevokeHipaaPrivacyNotice
               || draft.RevokeTreatmentConsent
               || draft.RevokeMarketingCommunications
               || draft.RevokePhiRelease
               || !draft.AllowPhoneCalls
               || !draft.AllowTextMessages
               || !draft.AllowEmailMessages
               || !draft.DryNeedlingEligible
               || draft.PelvicFloorTherapyEligible
               || !draft.PhiReleaseAuthorized
               || !draft.BillingConsentAuthorized;
    }

    private static void MergeLegacyRevocation(IntakeConsentPacket packet, string key, bool revoked)
    {
        // Only an affirmative legacy input adds evidence. An absent input cannot cancel it.
        if (revoked && !IntakeConsentJson.IsRevoked(packet, key))
            packet.RevokedConsentKeys.Add(key);
    }
}

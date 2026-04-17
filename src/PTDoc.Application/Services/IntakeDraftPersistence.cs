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
        return clone;
    }

    public static IntakeConsentPacket BuildCanonicalConsentPacket(IntakeResponseDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var packet = CloneConsentPacket(draft.ConsentPacket) ?? new IntakeConsentPacket();
        packet.HipaaAcknowledged ??= draft.HipaaAcknowledged;
        packet.TreatmentConsentAccepted ??= draft.ConsentToTreatAcknowledged;
        packet.PhiReleaseAuthorized ??= draft.PhiReleaseAuthorized;
        packet.CommunicationCallConsent ??= draft.AllowPhoneCalls;
        packet.CommunicationTextConsent ??= draft.AllowTextMessages;
        packet.CommunicationEmailConsent ??= draft.AllowEmailMessages;
        packet.DryNeedlingConsentAccepted ??= draft.DryNeedlingEligible;
        packet.PelvicFloorConsentAccepted ??= draft.PelvicFloorTherapyEligible;
        packet.CreditCardAuthorizationAccepted ??= draft.BillingConsentAuthorized;
        packet.FinalAttestationAccepted ??= draft.AccuracyConfirmed;

        if (!string.IsNullOrWhiteSpace(draft.PhoneNumber))
        {
            packet.CommunicationPhoneNumber = draft.PhoneNumber;
        }

        if (!string.IsNullOrWhiteSpace(draft.EmailAddress))
        {
            packet.CommunicationEmail = draft.EmailAddress;
        }

        SetRevoked(packet, "hipaaAcknowledged", draft.RevokeHipaaPrivacyNotice);
        SetRevoked(packet, "treatmentConsentAccepted", draft.RevokeTreatmentConsent);
        SetRevoked(packet, "phiReleaseAuthorized", draft.RevokePhiRelease);
        SetRevoked(packet, "communicationCallConsent", draft.RevokeMarketingCommunications);
        SetRevoked(packet, "communicationTextConsent", draft.RevokeMarketingCommunications);
        SetRevoked(packet, "communicationEmailConsent", draft.RevokeMarketingCommunications);

        return packet;
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

    public static void NormalizeCanonicalSupplementalSelections(IntakeResponseDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if ((draft.StructuredData?.ComorbidityIds?.Count ?? 0) > 0)
        {
            draft.SelectedComorbidities.Clear();
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

    private static void SetRevoked(IntakeConsentPacket packet, string key, bool revoked)
    {
        if (revoked)
        {
            if (!packet.RevokedConsentKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                packet.RevokedConsentKeys.Add(key);
            }

            return;
        }

        packet.RevokedConsentKeys.RemoveAll(existing => string.Equals(existing, key, StringComparison.OrdinalIgnoreCase));
    }
}

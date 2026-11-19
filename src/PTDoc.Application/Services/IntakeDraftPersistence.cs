using PTDoc.Application.Intake;
using System.Text.Json;

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
        => SerializePersistenceCopy(CreatePersistenceCopy(draft));

    private static string SerializePersistenceCopy(IntakeResponseDraft copy, IntakeConsentPacket? recordedConsents = null)
    {
        copy.ProfilePrefill = null; // Offers are refreshed from the authorized profile, not stored as answers.
        if (copy.ConsentPacket is null && recordedConsents is null)
            return JsonSerializer.Serialize(copy, SerializerOptions);

        var root = JsonSerializer.SerializeToNode(copy, SerializerOptions)!.AsObject();
        if (recordedConsents is not null)
        {
            // The locked snapshot uses exactly the packet validated for submission, including unanswered fields.
            root["consentPacket"] = IntakeConsentJson.ToJsonNode(recordedConsents);
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
        return SerializeSubmittedPersistenceJson(DeserializeDraft(responseJson), intakeId, patientId, submittedAtUtc, lastModifiedUtc, recordedConsents);
    }

    public static string SerializeSubmittedPersistenceJson(
        IntakeResponseDraft draft,
        Guid intakeId,
        Guid patientId,
        DateTime submittedAtUtc,
        DateTime lastModifiedUtc,
        IntakeConsentPacket? recordedConsents = null)
    {
        var copy = CreatePersistenceCopy(draft);
        copy.IntakeId = intakeId;
        copy.PatientId = patientId;
        copy.IsSubmitted = true;
        copy.IsLocked = true;
        copy.SubmittedAt = submittedAtUtc;
        copy.LastModifiedUtc = lastModifiedUtc;
        return SerializePersistenceCopy(copy, recordedConsents);
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
        // Resuming a recorded revocation must preserve unanswered consents through the next draft save.
        if (!packet.HasRevocationHistory)
        {
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
        }

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

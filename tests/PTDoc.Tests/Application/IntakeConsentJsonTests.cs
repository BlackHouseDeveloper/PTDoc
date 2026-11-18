using System.Text.Json;
using PTDoc.Application.Intake;
using PTDoc.Application.Services;

namespace PTDoc.Tests.Application;

[Trait("Category", "CoreCi")]
public sealed class IntakeConsentJsonTests
{
    [Theory]
    [InlineData("false")]
    [InlineData(null)]
    public void Normalize_PreservesLegacyRevokedEvidenceAndUnlistedRecipient(string? legacyRevokeFlag)
    {
        var mirror = legacyRevokeFlag is null ? "" : $",\"revokePhiRelease\":{legacyRevokeFlag}";
        var source = """{"consentPacket":{"revokedConsentKeys":["phiReleaseAuthorized"],"writtenRevocationReceived":true,"lastRevocationAtUtc":"2026-08-21T18:30:00Z","phiAuthorizedContactIds":["archived-recipient"],"evidenceRef":"synthetic-reference"}""" + mirror + "}";
        var draft = JsonSerializer.Deserialize<IntakeResponseDraft>(source, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var packet = IntakeDraftPersistence.BuildCanonicalConsentPacket(draft);
        Assert.True(IntakeConsentJson.TryParse(IntakeConsentJson.Serialize(packet), out var saved, out _));
        Assert.Null(saved.PhiReleaseAuthorized);
        Assert.False(IntakeConsentJson.IsPhiReleaseConsentActive(saved));
        Assert.True(saved.WrittenRevocationReceived);
        Assert.Equal(new DateTime(2026, 8, 21, 18, 30, 0, DateTimeKind.Utc), saved.LastRevocationAtUtc);
        Assert.Equal("archived-recipient", Assert.Single(saved.PhiAuthorizedContactIds));
        Assert.Equal("phiReleaseAuthorized", Assert.Single(saved.RevokedConsentKeys));
        Assert.Equal("synthetic-reference", saved.AdditionalData!["evidenceRef"].GetString());
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, null, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, null, false)]
    [InlineData(null, true, false)]
    [InlineData(null, false, false)]
    [InlineData(null, null, true)]
    public void RetainsRecordedRevocations_RequiresSameHistoricalAcceptance(bool? recorded, bool? incoming, bool retained)
    {
        var packet = new IntakeConsentPacket { PhiReleaseAuthorized = recorded };
        var timestamp = new DateTime(2026, 8, 21, 18, 30, 0, DateTimeKind.Utc);
        Assert.True(IntakeConsentJson.ApplyWrittenRevocation(packet, ["phiReleaseAuthorized"], timestamp).IsValid);
        var stored = IntakeConsentJson.Serialize(packet);
        packet.PhiReleaseAuthorized = incoming;

        Assert.Equal(retained, IntakeConsentJson.RetainsRecordedRevocations(stored, IntakeConsentJson.Serialize(packet)));
    }

    [Fact]
    public void TryParse_LegacyHipaaPayload_ReadsHipaaAcknowledgement()
    {
        const string json = "{" +
            "\"hipaaAcknowledged\":true" +
            "}";

        var parsed = IntakeConsentJson.TryParse(json, out var packet, out var errorMessage);

        Assert.True(parsed);
        Assert.Null(errorMessage);
        Assert.True(packet.HipaaAcknowledged);
        Assert.Empty(packet.AuthorizedContacts);
    }

    [Fact]
    public void TryParse_LegacyConsentToTreatAlias_HydratesCanonicalTreatmentConsent()
    {
        const string json = "{\"HipaaAcknowledged\":true,\"ConsentToTreat\":true}";

        var parsed = IntakeConsentJson.TryParse(json, out var packet, out var errorMessage);

        Assert.True(parsed);
        Assert.Null(errorMessage);
        Assert.True(packet.HipaaAcknowledged);
        Assert.True(packet.TreatmentConsentAccepted);
    }

    [Fact]
    public void Validate_MoreThanThreeAuthorizedContacts_ReturnsError()
    {
        var packet = new IntakeConsentPacket
        {
            AuthorizedContacts =
            [
                new AuthorizedContact { Name = "One", PhoneNumber = "111", Relationship = "Friend" },
                new AuthorizedContact { Name = "Two", PhoneNumber = "222", Relationship = "Parent" },
                new AuthorizedContact { Name = "Three", PhoneNumber = "333", Relationship = "Sibling" },
                new AuthorizedContact { Name = "Four", PhoneNumber = "444", Relationship = "Spouse" }
            ]
        };

        var result = IntakeConsentJson.Validate(packet);

        Assert.False(result.IsValid);
        Assert.Contains("authorizedContacts", result.Errors.Keys);
    }

    [Theory]
    [InlineData(true, false, null)]
    [InlineData(false, true, null)]
    public void Validate_PhoneOrTextConsentWithoutPhone_ReturnsError(bool callConsent, bool textConsent, string? phoneNumber)
    {
        var packet = new IntakeConsentPacket
        {
            CommunicationCallConsent = callConsent,
            CommunicationTextConsent = textConsent,
            CommunicationPhoneNumber = phoneNumber
        };

        var result = IntakeConsentJson.Validate(packet);

        Assert.False(result.IsValid);
        Assert.Contains("communicationPhoneNumber", result.Errors.Keys);
    }

    [Fact]
    public void Validate_EmailConsentWithoutEmail_ReturnsError()
    {
        var packet = new IntakeConsentPacket
        {
            CommunicationEmailConsent = true,
            CommunicationEmail = " "
        };

        var result = IntakeConsentJson.Validate(packet);

        Assert.False(result.IsValid);
        Assert.Contains("communicationEmail", result.Errors.Keys);
    }

    [Fact]
    public void Validate_SubmissionWithoutTreatmentConsent_ReturnsError()
    {
        var packet = new IntakeConsentPacket
        {
            HipaaAcknowledged = true,
            TreatmentConsentAccepted = false
        };

        var result = IntakeConsentJson.Validate(packet, requireHipaaAcknowledgement: true);

        Assert.False(result.IsValid);
        Assert.Contains("treatmentConsentAccepted", result.Errors.Keys);
    }

    [Fact]
    public void Validate_SubmissionWithRevokedRequiredConsent_ReturnsError()
    {
        var packet = new IntakeConsentPacket
        {
            HipaaAcknowledged = true,
            TreatmentConsentAccepted = true,
            RevokedConsentKeys = ["treatmentConsentAccepted"]
        };

        var result = IntakeConsentJson.Validate(packet, requireHipaaAcknowledgement: true);

        Assert.False(result.IsValid);
        Assert.Contains("treatmentConsentAccepted", result.Errors.Keys);
    }

    [Fact]
    public void CreateAuditSummary_ExcludesPhiFields()
    {
        var packet = new IntakeConsentPacket
        {
            HipaaAcknowledged = true,
            CommunicationCallConsent = true,
            CommunicationPhoneNumber = "555-1212",
            CommunicationEmailConsent = true,
            CommunicationEmail = "patient@example.com",
            AuthorizedContacts =
            [
                new AuthorizedContact { Name = "Pat Smith", PhoneNumber = "555-0100", Relationship = "Parent" }
            ]
        };

        var summary = IntakeConsentJson.CreateAuditSummary(packet);
        var json = JsonSerializer.Serialize(summary);

        Assert.True((bool)summary["HipaaAcknowledged"]);
        Assert.Equal(1, (int)summary["AuthorizedContactCount"]);
        Assert.DoesNotContain("Pat Smith", json, StringComparison.Ordinal);
        Assert.DoesNotContain("555-0100", json, StringComparison.Ordinal);
        Assert.DoesNotContain("555-1212", json, StringComparison.Ordinal);
        Assert.DoesNotContain("patient@example.com", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("communicationPhoneNumber", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplyWrittenRevocation_UnknownConsentKey_ReturnsValidationError()
    {
        var packet = new IntakeConsentPacket
        {
            HipaaAcknowledged = true
        };

        var result = IntakeConsentJson.ApplyWrittenRevocation(
            packet,
            ["unknownConsentKey"],
            DateTime.UtcNow);

        Assert.False(result.IsValid);
        Assert.Contains("consentKeys", result.Errors.Keys);
        Assert.True(packet.HipaaAcknowledged);
    }

    [Fact]
    public void ApplyWrittenRevocation_ValidKeys_PreservesOriginalAcceptanceAndRecordsRevocationMetadata()
    {
        var timestampUtc = DateTime.UtcNow;
        var packet = new IntakeConsentPacket
        {
            HipaaAcknowledged = true,
            CommunicationEmailConsent = true,
            FinalAttestationAccepted = true
        };

        var result = IntakeConsentJson.ApplyWrittenRevocation(
            packet,
            ["hipaaAcknowledged", "communicationEmailConsent"],
            timestampUtc);

        Assert.True(result.IsValid);
        Assert.True(packet.HipaaAcknowledged);
        Assert.True(packet.CommunicationEmailConsent);
        Assert.True(packet.FinalAttestationAccepted);
        Assert.True(packet.WrittenRevocationReceived);
        Assert.Equal(timestampUtc, packet.LastRevocationAtUtc);
        Assert.Contains("hipaaAcknowledged", packet.RevokedConsentKeys, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("communicationEmailConsent", packet.RevokedConsentKeys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Serialize_PhiRecipients_PersistsContactReferencesAndPrunesUnknownIds()
    {
        var selected = new AuthorizedContact
        {
            Id = "contact-one",
            Name = "Jane Smith",
            PhoneNumber = "555-0100",
            Relationship = "Spouse"
        };
        var packet = new IntakeConsentPacket
        {
            PhiReleaseAuthorized = true,
            AuthorizedContacts = [selected],
            PhiAuthorizedContactIds = [selected.Id, "missing-contact"]
        };

        var json = IntakeConsentJson.Serialize(packet);
        var parsed = IntakeConsentJson.TryParse(json, out var roundTrip, out var error);

        Assert.True(parsed);
        Assert.Null(error);
        Assert.Equal("contact-one", Assert.Single(roundTrip.PhiAuthorizedContactIds));
        Assert.Equal("contact-one", Assert.Single(roundTrip.AuthorizedContacts).Id);
        Assert.True(IntakeConsentJson.Validate(roundTrip).IsValid);
    }

    [Fact]
    public void Validate_PhiReleaseWithContactsButNoSelectedRecipient_ReturnsError()
    {
        var packet = new IntakeConsentPacket
        {
            PhiReleaseAuthorized = true,
            AuthorizedContacts =
            [
                new AuthorizedContact
                {
                    Name = "Jane Smith",
                    PhoneNumber = "555-0100",
                    Relationship = "Spouse"
                }
            ]
        };

        var result = IntakeConsentJson.Validate(packet);

        Assert.False(result.IsValid);
        Assert.Contains("phiAuthorizedContactIds", result.Errors.Keys);
    }
}

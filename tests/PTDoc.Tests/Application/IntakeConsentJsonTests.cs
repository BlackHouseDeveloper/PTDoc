using System.Text.Json;
using PTDoc.Application.Intake;

namespace PTDoc.Tests.Application;

public sealed class IntakeConsentJsonTests
{
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
        Assert.DoesNotContain("patient@example.com", json, StringComparison.OrdinalIgnoreCase);
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
}
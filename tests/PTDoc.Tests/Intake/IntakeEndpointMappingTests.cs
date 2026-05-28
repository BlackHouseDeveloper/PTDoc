using System.Text.Json;
using PTDoc.Api.Intake;
using PTDoc.Application.Services;
using PTDoc.Core.Models;

namespace PTDoc.Tests.Intake;

[Trait("Category", "CoreCi")]
public sealed class IntakeEndpointMappingTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void NormalizeSubmittedPersistenceJson_StampsSubmittedMetadataAndPreservesDraftFields()
    {
        var intakeId = Guid.NewGuid();
        var patientId = Guid.NewGuid();
        var submittedAt = new DateTime(2026, 5, 27, 14, 30, 0, DateTimeKind.Utc);
        var lastModifiedUtc = submittedAt.AddSeconds(2);
        var responseJson = JsonSerializer.Serialize(new IntakeResponseDraft
        {
            FullName = "Beta Submitted",
            FunctionalLimitations = "Difficulty with stairs.",
            IsLocked = false,
            IsSubmitted = false
        }, JsonOptions);

        var normalized = IntakeDraftPersistence.NormalizeSubmittedPersistenceJson(
            responseJson,
            intakeId,
            patientId,
            submittedAt,
            lastModifiedUtc);

        using var document = JsonDocument.Parse(normalized);
        Assert.Equal(intakeId, document.RootElement.GetProperty("intakeId").GetGuid());
        Assert.Equal(patientId, document.RootElement.GetProperty("patientId").GetGuid());
        Assert.True(document.RootElement.GetProperty("isLocked").GetBoolean());
        Assert.True(document.RootElement.GetProperty("isSubmitted").GetBoolean());
        Assert.Equal(submittedAt, document.RootElement.GetProperty("submittedAt").GetDateTime());
        Assert.Equal(lastModifiedUtc, document.RootElement.GetProperty("lastModifiedUtc").GetDateTime());
        Assert.Equal("Beta Submitted", document.RootElement.GetProperty("fullName").GetString());
        Assert.Equal("Difficulty with stairs.", document.RootElement.GetProperty("functionalLimitations").GetString());
    }

    [Fact]
    public void ApplySubmittedIntakePatientFields_MapsSupportedDraftFieldsToPatient()
    {
        var patient = new Patient
        {
            Id = Guid.NewGuid(),
            FirstName = "Existing",
            LastName = "Patient",
            DateOfBirth = new DateTime(1980, 1, 1)
        };

        var intake = new IntakeForm
        {
            Id = Guid.NewGuid(),
            PatientId = patient.Id,
            Patient = patient,
            ResponseJson = JsonSerializer.Serialize(new IntakeResponseDraft
            {
                FullName = "Beta Submitted",
                DateOfBirth = new DateTime(1990, 2, 3),
                EmailAddress = "beta.submitted@example.com",
                PhoneNumber = "555-0100",
                AddressLine1 = "100 Beta Validation Way",
                City = "San Diego",
                StateOrProvince = "CA",
                PostalCode = "92101",
                EmergencyContactName = "Emergency Contact",
                EmergencyContactPhone = "555-0101",
                ReferringDoctorName = "Dr. Referral",
                ReferringDoctorNpi = "1234567890",
                InsuranceCompanyName = "PFPT Beta PPO",
                MemberOrPolicyNumber = "BETA001",
                PayerType = "Commercial",
                InsuranceCoverageType = "Primary",
                PrimaryDoctorName = "Dr. Primary",
                FunctionalLimitations = "Difficulty with stairs."
            }, JsonOptions),
            PainMapData = "{}",
            Consents = "{}",
            TemplateVersion = "1.0",
            ModifiedByUserId = Guid.NewGuid()
        };

        IntakeEndpoints.ApplySubmittedIntakePatientFields(intake);

        Assert.Equal("Beta", patient.FirstName);
        Assert.Equal("Submitted", patient.LastName);
        Assert.Equal(new DateTime(1990, 2, 3), patient.DateOfBirth);
        Assert.Equal("beta.submitted@example.com", patient.Email);
        Assert.Equal("555-0100", patient.Phone);
        Assert.Equal("100 Beta Validation Way", patient.AddressLine1);
        Assert.Equal("San Diego", patient.City);
        Assert.Equal("CA", patient.State);
        Assert.Equal("92101", patient.ZipCode);
        Assert.Equal("Emergency Contact", patient.EmergencyContactName);
        Assert.Equal("555-0101", patient.EmergencyContactPhone);
        Assert.Equal("Dr. Referral", patient.ReferringPhysician);
        Assert.Equal("1234567890", patient.PhysicianNpi);
        Assert.Contains("PFPT Beta PPO", patient.PayerInfoJson, StringComparison.Ordinal);
        using var payerInfoJson = JsonDocument.Parse(patient.PayerInfoJson);
        Assert.Equal("Commercial", payerInfoJson.RootElement.GetProperty("providerType").GetString());
        Assert.Equal("BETA001", payerInfoJson.RootElement.GetProperty("memberIdPolicyNumber").GetString());
        Assert.Equal("Primary", payerInfoJson.RootElement.GetProperty("insurancePriority").GetString());
        Assert.DoesNotContain("Dr. Primary", patient.PayerInfoJson, StringComparison.Ordinal);
        Assert.DoesNotContain("Difficulty with stairs.", patient.PayerInfoJson, StringComparison.Ordinal);
        Assert.Equal(intake.ModifiedByUserId, patient.ModifiedByUserId);
        Assert.Equal(SyncState.Pending, patient.SyncState);
        Assert.True(patient.LastModifiedUtc > DateTime.MinValue);
    }

    [Fact]
    public void ApplySubmittedIntakePatientFields_PreservesExistingPayerInfo_WhenDraftHasNoPayerFields()
    {
        var existingPayerInfo = JsonSerializer.Serialize(new
        {
            PayerType = "Medicare",
            InsuranceCompanyName = "Existing Plan",
            MemberOrPolicyNumber = "EXISTING-001"
        }, JsonOptions);
        var patient = new Patient
        {
            Id = Guid.NewGuid(),
            FirstName = "Existing",
            LastName = "Payer",
            PayerInfoJson = existingPayerInfo
        };

        var intake = new IntakeForm
        {
            Id = Guid.NewGuid(),
            PatientId = patient.Id,
            Patient = patient,
            ResponseJson = JsonSerializer.Serialize(new IntakeResponseDraft
            {
                FullName = "Existing Payer",
                EmailAddress = "payer@example.com",
                PhoneNumber = "555-0112"
            }, JsonOptions),
            PainMapData = "{}",
            Consents = "{}",
            TemplateVersion = "1.0",
            ModifiedByUserId = Guid.NewGuid()
        };

        IntakeEndpoints.ApplySubmittedIntakePatientFields(intake);

        Assert.Equal(existingPayerInfo, patient.PayerInfoJson);
        Assert.Equal("payer@example.com", patient.Email);
        Assert.Equal("555-0112", patient.Phone);
        Assert.Equal(intake.ModifiedByUserId, patient.ModifiedByUserId);
        Assert.Equal(SyncState.Pending, patient.SyncState);
    }

    [Fact]
    public void ApplySubmittedIntakePatientFields_DoesNotOverwritePhysicianNpi_WhenReferringDoctorNpiInvalid()
    {
        var patient = new Patient
        {
            Id = Guid.NewGuid(),
            FirstName = "Existing",
            LastName = "Npi",
            PhysicianNpi = "1234567890"
        };

        var intake = new IntakeForm
        {
            Id = Guid.NewGuid(),
            PatientId = patient.Id,
            Patient = patient,
            ResponseJson = JsonSerializer.Serialize(new IntakeResponseDraft
            {
                FullName = "Existing Npi",
                ReferringDoctorNpi = "abc123"
            }, JsonOptions),
            PainMapData = "{}",
            Consents = "{}",
            TemplateVersion = "1.0",
            ModifiedByUserId = Guid.NewGuid()
        };

        IntakeEndpoints.ApplySubmittedIntakePatientFields(intake);

        Assert.Equal("1234567890", patient.PhysicianNpi);
    }
}

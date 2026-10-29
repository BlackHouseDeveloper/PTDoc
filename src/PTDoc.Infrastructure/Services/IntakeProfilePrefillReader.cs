using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Intake;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Infrastructure.Services;

/// <summary>Reads profile values using both patient and clinic identity; never mutates an intake on read.</summary>
public static class IntakeProfilePrefillReader
{
    public static async Task<IntakeProfilePrefill?> ReadAsync(
        ApplicationDbContext db, Guid patientId, Guid? clinicId, CancellationToken cancellationToken = default)
    {
        var patient = await db.Patients.AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == patientId && p.ClinicId == clinicId && !p.IsArchived, cancellationToken);
        if (patient is null) return null;

        var policies = await db.PatientInsurancePolicies.AsNoTracking()
            .Where(p => p.PatientId == patientId && p.ClinicId == clinicId && !p.IsArchived
                && p.Status == InsurancePolicyStatus.Active)
            .ToListAsync(cancellationToken);
        var profile = new IntakeProfilePrefill
        {
            FullName = $"{patient.FirstName} {patient.LastName}".Trim(),
            DateOfBirth = patient.DateOfBirth == default ? null : patient.DateOfBirth,
            EmailAddress = patient.Email,
            PhoneNumber = patient.Phone,
            AddressLine1 = patient.AddressLine1,
            AddressLine2 = patient.AddressLine2,
            City = patient.City,
            StateOrProvince = patient.State,
            PostalCode = patient.ZipCode,
            EmergencyContactName = patient.EmergencyContactName,
            EmergencyContactPhone = patient.EmergencyContactPhone,
            SourceModifiedUtc = patient.LastModifiedUtc
        };
        // Only unambiguous normalized policies are used. Legacy payer values are a fallback
        // only when that coverage priority has no normalized record, including archived history.
        var allPriorities = await db.PatientInsurancePolicies.AsNoTracking()
            .Where(p => p.PatientId == patientId && p.ClinicId == clinicId)
            .Select(p => p.CoveragePriority).Distinct().ToListAsync(cancellationToken);
        IntakeResponseDraft legacy;
        try
        {
            legacy = JsonSerializer.Deserialize<IntakeResponseDraft>(patient.PayerInfoJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? new();
            using var json = JsonDocument.Parse(patient.PayerInfoJson);
            if (json.RootElement.ValueKind == JsonValueKind.Object)
            {
                legacy.MemberOrPolicyNumber ??= ReadAlias(json.RootElement, "memberIdPolicyNumber");
                legacy.SecondaryMemberOrPolicyNumber ??= ReadAlias(json.RootElement, "secondaryMemberIdPolicyNumber");
                legacy.PayerType ??= ReadAlias(json.RootElement, "providerType");
                legacy.PayerType = IntakeProfilePrefill.NormalizeProviderType(legacy.PayerType) ?? legacy.PayerType;
            }
        }
        catch (JsonException) { legacy = new(); }

        var primary = policies.Where(p => p.CoveragePriority == InsuranceCoveragePriority.Primary).ToArray();
        if (primary.Length == 1)
        {
            var p = primary[0];
            profile.PrimaryPolicyId = p.Id;
            profile.PrimaryPolicyModifiedUtc = p.LastModifiedUtc;
            profile.InsuranceCompanyName = p.CarrierDisplayName;
            profile.MemberOrPolicyNumber = p.MemberOrPolicyNumber;
            profile.GroupNumber = p.GroupNumber;
            profile.PayerType = p.PayerType switch
            {
                InsurancePayerType.WorkersCompensation => "Workers' Compensation",
                InsurancePayerType.MotorVehicle => "Motor Vehicle",
                InsurancePayerType.SelfPay => "Self Pay",
                InsurancePayerType.Tricare => "TRICARE",
                InsurancePayerType.Other => null,
                _ => p.PayerType.ToString()
            };
            profile.AdjusterName = p.AdjusterName;
            profile.AdjusterPhone = p.AdjusterPhone;
            profile.AdjusterEmail = p.AdjusterEmail;
            profile.AdjusterFax = p.AdjusterFax;
            if (p.LastModifiedUtc > profile.SourceModifiedUtc) profile.SourceModifiedUtc = p.LastModifiedUtc;
        }
        else if (!allPriorities.Contains(InsuranceCoveragePriority.Primary))
        {
            profile.InsuranceCompanyName = legacy.InsuranceCompanyName;
            profile.MemberOrPolicyNumber = legacy.MemberOrPolicyNumber;
            profile.GroupNumber = legacy.GroupNumber;
            profile.PayerType = legacy.PayerType;
            profile.AdjusterName = legacy.AdjusterName;
            profile.AdjusterPhone = legacy.AdjusterPhone;
            profile.AdjusterEmail = legacy.AdjusterEmail;
            profile.AdjusterFax = legacy.AdjusterFax;
        }
        var secondary = policies.Where(p => p.CoveragePriority == InsuranceCoveragePriority.Secondary).ToArray();
        if (secondary.Length == 1)
        {
            var p = secondary[0];
            profile.SecondaryPolicyId = p.Id;
            profile.SecondaryPolicyModifiedUtc = p.LastModifiedUtc;
            profile.SecondaryInsuranceCompanyName = p.CarrierDisplayName;
            profile.SecondaryMemberOrPolicyNumber = p.MemberOrPolicyNumber;
            profile.SecondaryGroupNumber = p.GroupNumber;
            if (p.LastModifiedUtc > profile.SourceModifiedUtc) profile.SourceModifiedUtc = p.LastModifiedUtc;
        }
        else if (!allPriorities.Contains(InsuranceCoveragePriority.Secondary))
        {
            profile.SecondaryInsuranceCompanyName = legacy.SecondaryInsuranceCompanyName;
            profile.SecondaryMemberOrPolicyNumber = legacy.SecondaryMemberOrPolicyNumber;
            profile.SecondaryGroupNumber = legacy.SecondaryGroupNumber;
        }
        profile.PrimaryPolicyAmbiguous = primary.Length > 1;
        profile.SecondaryPolicyAmbiguous = secondary.Length > 1;
        profile.Fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            JsonSerializer.SerializeToUtf8Bytes(profile)));
        return profile;
    }

    private static string? ReadAlias(JsonElement value, string key) => value.EnumerateObject()
        .Where(property => string.Equals(property.Name, key, StringComparison.OrdinalIgnoreCase)
            && property.Value.ValueKind == JsonValueKind.String)
        .Select(property => property.Value.GetString()).FirstOrDefault();
}

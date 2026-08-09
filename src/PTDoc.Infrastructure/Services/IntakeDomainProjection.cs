using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Services;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Infrastructure.Services;

/// <summary>Dual-writes submitted intake care-team and payer data into normalized aggregates.</summary>
public static class IntakeDomainProjection
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };

    public static Task UpsertAsync(ApplicationDbContext db, Patient patient, string? responseJson, Guid userId, CancellationToken ct)
    {
        IntakeResponseDraft draft;
        try { draft = JsonSerializer.Deserialize<IntakeResponseDraft>(responseJson ?? "{}", JsonOptions) ?? new(); }
        catch (JsonException) { return Task.CompletedTask; }
        return UpsertAsync(db, patient, draft, userId, ct);
    }

    public static async Task UpsertPatientRecordAsync(ApplicationDbContext db, Patient patient, string? payerInfoJson, string? referringProviderName, string? referringProviderNpi, Guid userId, CancellationToken ct)
    {
        IntakeResponseDraft draft;
        try { draft = JsonSerializer.Deserialize<IntakeResponseDraft>(payerInfoJson ?? "{}", JsonOptions) ?? new(); }
        catch (JsonException) { draft = new(); }
        HydrateLegacyPayerAliases(draft, payerInfoJson);
        if (!string.IsNullOrWhiteSpace(referringProviderName)) draft.ReferringDoctorName = referringProviderName;
        if (!string.IsNullOrWhiteSpace(referringProviderNpi)) draft.ReferringDoctorNpi = referringProviderNpi;
        if (string.IsNullOrWhiteSpace(draft.AuthorizationNumber)) draft.AuthorizationNumber = patient.AuthorizationNumber;
        await UpsertAsync(db, patient, draft, userId, ct);
        await UpsertAuthorizationHistoryAsync(db, patient, payerInfoJson, userId, ct);
    }

    public static async Task UpsertAsync(ApplicationDbContext db, Patient patient, IntakeResponseDraft draft, Guid userId, CancellationToken ct)
    {
        await UpsertProviderAsync(db, patient, draft.PrimaryDoctorName, draft.PrimaryDoctorPhone, null, PatientProviderRole.PrimaryCare, userId, ct);
        await UpsertProviderAsync(db, patient, draft.ReferringDoctorName, draft.ReferringDoctorPhone, draft.ReferringDoctorNpi, PatientProviderRole.Referring, userId, ct);
        await UpsertPolicyAsync(db, patient, InsuranceCoveragePriority.Primary, draft.InsuranceCompanyName, draft.MemberOrPolicyNumber, draft.GroupNumber, draft.PayerType, draft.AdjusterName, draft.AdjusterPhone, draft.AdjusterEmail, draft.AdjusterFax, userId, ct);
        await UpsertPolicyAsync(db, patient, InsuranceCoveragePriority.Secondary, draft.SecondaryInsuranceCompanyName, draft.SecondaryMemberOrPolicyNumber, draft.SecondaryGroupNumber, null, null, null, null, null, userId, ct);
        await UpsertAuthorizationAsync(db, patient, draft, userId, ct);
    }

    private static async Task UpsertProviderAsync(ApplicationDbContext db, Patient patient, string? displayName, string? phone, string? npi, PatientProviderRole role, Guid userId, CancellationToken ct)
    {
        displayName = Trim(displayName); phone = Trim(phone); npi = Trim(npi);
        if (displayName is null) return;
        var existingRelationship = db.PatientProviderRelationships.Local.FirstOrDefault(r => r.PatientId == patient.Id && r.Role == role && !r.IsArchived)
            ?? await db.PatientProviderRelationships.Include(r => r.Provider).FirstOrDefaultAsync(r => r.PatientId == patient.Id && r.Role == role && !r.IsArchived, ct);
        if (existingRelationship is not null) return;
        ProviderDirectoryEntry? provider = null;
        if (npi is not null) provider = await db.ProviderDirectoryEntries.FirstOrDefaultAsync(e => e.Npi == npi && !e.IsArchived && e.Status == ProviderDirectoryStatus.Active, ct);
        if (provider is null)
        {
            var parts = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var last = parts[^1]; var first = parts.Length > 1 ? string.Join(' ', parts[..^1]) : string.Empty;
            provider = await db.ProviderDirectoryEntries.FirstOrDefaultAsync(e => e.FirstName == first && e.LastName == last && e.Phone == phone && !e.IsArchived && e.Status == ProviderDirectoryStatus.Active, ct);
            var localPatientProviderIds = db.PatientProviderRelationships.Local
                .Where(relationship => relationship.PatientId == patient.Id && !relationship.IsArchived)
                .Select(relationship => relationship.ProviderDirectoryEntryId)
                .ToHashSet();
            provider ??= db.ProviderDirectoryEntries.Local.FirstOrDefault(entry =>
                localPatientProviderIds.Contains(entry.Id) && MatchesProvider(entry, first, last, phone, npi));
            if (provider is null)
            {
                var patientProviders = await db.PatientProviderRelationships
                    .Where(relationship => relationship.PatientId == patient.Id && !relationship.IsArchived)
                    .Select(relationship => relationship.Provider!)
                    .Where(entry => !entry.IsArchived)
                    .ToListAsync(ct);
                provider = patientProviders.FirstOrDefault(entry => MatchesProvider(entry, first, last, phone, npi));
            }
            if (provider is null) { provider = new() { ClinicId = patient.ClinicId, FirstName = first, LastName = last, Npi = npi, Phone = phone, Status = ProviderDirectoryStatus.Pending, SubmissionSource = ProviderSubmissionSource.PatientIntake, SubmittedAtUtc = DateTime.UtcNow, LastModifiedUtc = DateTime.UtcNow, ModifiedByUserId = userId, SyncState = SyncState.Pending }; db.ProviderDirectoryEntries.Add(provider); }
            else if (provider.Status == ProviderDirectoryStatus.Pending && string.IsNullOrWhiteSpace(provider.Npi) && npi is not null) provider.Npi = npi;
        }
        db.PatientProviderRelationships.Add(new() { PatientId = patient.Id, ProviderDirectoryEntryId = provider.Id, ClinicId = patient.ClinicId, Role = role, IsPrimary = true, LastModifiedUtc = DateTime.UtcNow, ModifiedByUserId = userId, SyncState = SyncState.Pending });
    }

    private static bool MatchesProvider(ProviderDirectoryEntry entry, string first, string last, string? phone, string? npi)
        => npi is not null && !string.IsNullOrWhiteSpace(entry.Npi)
            ? string.Equals(Trim(entry.Npi), npi, StringComparison.OrdinalIgnoreCase)
            : string.Equals(Trim(entry.FirstName), first, StringComparison.OrdinalIgnoreCase)
              && string.Equals(Trim(entry.LastName), last, StringComparison.OrdinalIgnoreCase)
              && string.Equals(Trim(entry.Phone), phone, StringComparison.OrdinalIgnoreCase);

    private static async Task UpsertPolicyAsync(ApplicationDbContext db, Patient patient, InsuranceCoveragePriority priority, string? carrier, string? member, string? group, string? payerType, string? adjusterName, string? adjusterPhone, string? adjusterEmail, string? adjusterFax, Guid userId, CancellationToken ct)
    {
        carrier = Trim(carrier); member = Trim(member); group = Trim(group); var normalizedPayerType = string.IsNullOrWhiteSpace(payerType) ? InsurancePayerType.Other : ParsePayerType(payerType); if (carrier is null && member is null && group is null && normalizedPayerType != InsurancePayerType.SelfPay) return;
        var policy = await db.PatientInsurancePolicies.FirstOrDefaultAsync(p => p.PatientId == patient.Id && p.CoveragePriority == priority && !p.IsArchived && p.Status == InsurancePolicyStatus.Active, ct);
        if (policy is null) { policy = new() { PatientId = patient.Id, ClinicId = patient.ClinicId, CoveragePriority = priority, Status = InsurancePolicyStatus.Active }; db.PatientInsurancePolicies.Add(policy); }
        if (carrier is not null) { policy.CarrierDisplayName = carrier; policy.CarrierKey = string.Join('-', carrier.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries)); }
        if (member is not null) policy.MemberOrPolicyNumber = member; if (group is not null) policy.GroupNumber = group;
        if (!string.IsNullOrWhiteSpace(payerType)) policy.PayerType = normalizedPayerType;
        if (Trim(adjusterName) is { } name) policy.AdjusterName = name; if (Trim(adjusterPhone) is { } phone) policy.AdjusterPhone = phone; if (Trim(adjusterEmail) is { } email) policy.AdjusterEmail = email; if (Trim(adjusterFax) is { } fax) policy.AdjusterFax = fax;
        policy.LastModifiedUtc = DateTime.UtcNow; policy.ModifiedByUserId = userId; policy.SyncState = SyncState.Pending;
    }

    private static async Task UpsertAuthorizationAsync(ApplicationDbContext db, Patient patient, IntakeResponseDraft draft, Guid userId, CancellationToken ct)
    {
        if (new[] { draft.AuthorizationNumber, draft.AuthorizationStatus, draft.DateAuthorizationReceived, draft.AuthorizationStartDate, draft.AuthorizationEndDate, draft.NumberOfVisitsOrUnitsAuthorized, draft.TotalVisitLimit, draft.VisitsUsed, draft.ReAuthorizationDueDate, draft.VisitAlertThreshold, draft.AuthorizationNotes, draft.NotesComments }.All(string.IsNullOrWhiteSpace)) return;
        var policy = db.PatientInsurancePolicies.Local.FirstOrDefault(policy => policy.PatientId == patient.Id && policy.CoveragePriority == InsuranceCoveragePriority.Primary && !policy.IsArchived)
            ?? await db.PatientInsurancePolicies.FirstOrDefaultAsync(policy => policy.PatientId == patient.Id && policy.CoveragePriority == InsuranceCoveragePriority.Primary && !policy.IsArchived, ct);
        if (policy is null) return;
        var authorization = db.PatientInsuranceAuthorizations.Local.FirstOrDefault(value => value.PatientInsurancePolicyId == policy.Id && !value.IsArchived)
            ?? await db.PatientInsuranceAuthorizations.FirstOrDefaultAsync(value => value.PatientInsurancePolicyId == policy.Id && !value.IsArchived, ct);
        if (authorization is null) { authorization = new() { PatientInsurancePolicyId = policy.Id, PatientId = patient.Id, ClinicId = patient.ClinicId }; db.PatientInsuranceAuthorizations.Add(authorization); }
        if (!string.IsNullOrWhiteSpace(draft.AuthorizationType)) authorization.AuthorizationType = draft.AuthorizationType.Contains("referral", StringComparison.OrdinalIgnoreCase) ? InsuranceAuthorizationType.Referral : InsuranceAuthorizationType.Authorization;
        if (Trim(draft.AuthorizationNumber) is { } reference) authorization.ReferenceNumber = reference;
        if (Enum.TryParse<InsuranceAuthorizationStatus>(draft.AuthorizationStatus, true, out var status)) authorization.Status = status;
        if (ParseDate(draft.DateAuthorizationReceived) is { } received) authorization.ReceivedDate = received;
        if (ParseDate(draft.AuthorizationStartDate) is { } start) authorization.StartDate = start;
        if (ParseDate(draft.AuthorizationEndDate) is { } end) authorization.EndDate = end;
        if ((ParseDecimal(draft.NumberOfVisitsOrUnitsAuthorized) ?? ParseDecimal(draft.TotalVisitLimit)) is { } authorized) authorization.AuthorizedUnits = authorized;
        if (ParseDecimal(draft.VisitsUsed) is { } used) authorization.UsedUnits = used;
        if (Enum.TryParse<InsuranceVisitLimitPeriod>(draft.VisitLimitPeriod, true, out var period)) authorization.VisitLimitPeriod = period;
        if (ParseDate(draft.ReAuthorizationDueDate) is { } reauthorization) authorization.ReauthorizationDueDate = reauthorization;
        if (int.TryParse(draft.VisitAlertThreshold, NumberStyles.Integer, CultureInfo.InvariantCulture, out var threshold)) authorization.VisitAlertThreshold = threshold;
        if ((Trim(draft.AuthorizationNotes) ?? Trim(draft.NotesComments)) is { } notes) authorization.Notes = notes;
        authorization.LastModifiedUtc = DateTime.UtcNow; authorization.ModifiedByUserId = userId; authorization.SyncState = SyncState.Pending;
    }

    private static async Task UpsertAuthorizationHistoryAsync(ApplicationDbContext db, Patient patient, string? payerInfoJson, Guid userId, CancellationToken ct)
    {
        JsonDocument document;
        try { document = JsonDocument.Parse(payerInfoJson ?? "{}"); } catch (JsonException) { return; }
        using (document)
        {
            if (!document.RootElement.TryGetProperty("authorizationReferralHistory", out var history) || history.ValueKind != JsonValueKind.Array) return;
            var policy = db.PatientInsurancePolicies.Local.FirstOrDefault(value => value.PatientId == patient.Id && value.CoveragePriority == InsuranceCoveragePriority.Primary && !value.IsArchived)
                ?? await db.PatientInsurancePolicies.FirstOrDefaultAsync(value => value.PatientId == patient.Id && value.CoveragePriority == InsuranceCoveragePriority.Primary && !value.IsArchived, ct);
            if (policy is null) return;
            foreach (var item in history.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var recordType = GetJson(item, "recordType"); var reference = GetJson(item, "referenceNumber"); var start = ParseDate(GetJson(item, "startDate")); var end = ParseDate(GetJson(item, "endDate")); var type = recordType?.Contains("referral", StringComparison.OrdinalIgnoreCase) == true ? InsuranceAuthorizationType.Referral : InsuranceAuthorizationType.Authorization;
                var existing = db.PatientInsuranceAuthorizations.Local.FirstOrDefault(value => value.PatientInsurancePolicyId == policy.Id && !value.IsArchived && value.AuthorizationType == type && value.ReferenceNumber == reference && value.StartDate == start && value.EndDate == end)
                    ?? await db.PatientInsuranceAuthorizations.FirstOrDefaultAsync(value => value.PatientInsurancePolicyId == policy.Id && !value.IsArchived && value.AuthorizationType == type && value.ReferenceNumber == reference && value.StartDate == start && value.EndDate == end, ct);
                if (existing is null) { existing = new() { PatientInsurancePolicyId = policy.Id, PatientId = patient.Id, ClinicId = patient.ClinicId, AuthorizationType = type, ReferenceNumber = reference, StartDate = start, EndDate = end }; db.PatientInsuranceAuthorizations.Add(existing); }
                if (Enum.TryParse<InsuranceAuthorizationStatus>(GetJson(item, "status"), true, out var status)) existing.Status = status;
                if (ParseDecimal(GetJson(item, "visitsOrUnitsAuthorized")) is { } units) existing.AuthorizedUnits = units;
                if (Trim(GetJson(item, "notes")) is { } notes) existing.Notes = notes;
                existing.LastModifiedUtc = DateTime.UtcNow; existing.ModifiedByUserId = userId; existing.SyncState = SyncState.Pending;
            }
        }
    }

    private static InsurancePayerType ParsePayerType(string value) => value.Trim().ToLowerInvariant() switch { "commercial" => InsurancePayerType.Commercial, "medicare" => InsurancePayerType.Medicare, "medicaid" => InsurancePayerType.Medicaid, "workers' compensation" => InsurancePayerType.WorkersCompensation, "motor vehicle" => InsurancePayerType.MotorVehicle, "liability" => InsurancePayerType.Liability, "self pay" => InsurancePayerType.SelfPay, _ => InsurancePayerType.Other };
    private static void HydrateLegacyPayerAliases(IntakeResponseDraft draft, string? payerInfoJson)
    {
        try
        {
            using var document = JsonDocument.Parse(payerInfoJson ?? "{}");
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return;
            draft.MemberOrPolicyNumber ??= GetJson(root, "memberOrPolicyNumber", "memberIdPolicyNumber");
            draft.SecondaryMemberOrPolicyNumber ??= GetJson(root, "secondaryMemberOrPolicyNumber", "secondaryMemberIdPolicyNumber");
        }
        catch (JsonException)
        {
            // The caller already treats malformed legacy JSON as an empty partial update.
        }
    }
    private static DateTime? ParseDate(string? value) => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed) ? parsed : null;
    private static decimal? ParseDecimal(string? value) => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    private static string? GetJson(JsonElement element, params string[] propertyNames)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (propertyNames.Any(name => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) && property.Value.ValueKind == JsonValueKind.String)
                return Trim(property.Value.GetString());
        }
        return null;
    }
    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

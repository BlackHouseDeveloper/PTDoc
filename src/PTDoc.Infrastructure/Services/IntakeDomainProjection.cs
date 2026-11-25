using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Services;
using PTDoc.Application.Intake;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Infrastructure.Services;

/// <summary>Dual-writes submitted intake care-team and payer data into normalized aggregates.</summary>
public static class IntakeDomainProjection
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
    public const string StalePolicySourceMessage = "Saved insurance has changed since this intake was prefilled. Review the current policies before submitting.";

    public static Task<bool> HasCurrentPolicySourcesAsync(ApplicationDbContext db, Guid patientId, Guid? clinicId, string? responseJson, CancellationToken ct)
    {
        IntakeResponseDraft draft;
        try { draft = JsonSerializer.Deserialize<IntakeResponseDraft>(responseJson ?? "{}", JsonOptions) ?? new(); }
        catch (JsonException) { return Task.FromResult(false); }
        return HasCurrentPolicySourcesAsync(db, patientId, clinicId, draft, ct);
    }

    public static async Task<bool> HasCurrentPolicySourcesAsync(ApplicationDbContext db, Guid patientId, Guid? clinicId, IntakeResponseDraft draft, CancellationToken ct)
    {
        var profile = await IntakeProfilePrefillReader.ReadAsync(db, patientId, clinicId, ct);
        if (profile is null) return false;

        static bool IsCurrent(Guid? acceptedId, DateTime? acceptedVersion, string? acceptedLegacy,
            Guid? currentId, DateTime? currentVersion, string? currentLegacy, bool ambiguous)
            => !ambiguous && acceptedId == currentId && (acceptedId.HasValue
                ? acceptedVersion.HasValue && acceptedVersion == currentVersion
                : string.Equals(acceptedLegacy, currentLegacy, StringComparison.Ordinal));

        return (draft.OmitPrimaryPolicy || IsCurrent(draft.PrefilledPrimaryPolicyId, draft.PrefilledPrimaryPolicyModifiedUtc,
                draft.PrefilledPrimaryLegacyPayerFingerprint, profile.PrimaryPolicyId, profile.PrimaryPolicyModifiedUtc,
                profile.PrimaryLegacyPayerFingerprint, profile.PrimaryPolicyAmbiguous))
            && (draft.OmitSecondaryPolicy || IsCurrent(draft.PrefilledSecondaryPolicyId, draft.PrefilledSecondaryPolicyModifiedUtc,
                draft.PrefilledSecondaryLegacyPayerFingerprint, profile.SecondaryPolicyId, profile.SecondaryPolicyModifiedUtc,
                profile.SecondaryLegacyPayerFingerprint, profile.SecondaryPolicyAmbiguous));
    }

    private static bool IsPolicyVersionCurrent(PatientInsurancePolicy policy, DateTime? sourceModifiedUtc)
        => sourceModifiedUtc.HasValue && policy.LastModifiedUtc == sourceModifiedUtc.Value;

    public static Task UpsertAsync(ApplicationDbContext db, Patient patient, string? responseJson, Guid userId, CancellationToken ct)
    {
        IntakeResponseDraft draft;
        try { draft = JsonSerializer.Deserialize<IntakeResponseDraft>(responseJson ?? "{}", JsonOptions) ?? new(); }
        catch (JsonException) { return Task.CompletedTask; }
        return UpsertAsync(db, patient, draft, userId, ct);
    }

    public static async Task UpsertPatientRecordAsync(ApplicationDbContext db, Patient patient, string? payerInfoJson, string? referringProviderName, string? referringProviderNpi, Guid userId, CancellationToken ct, string? previousPayerInfoJson = null, bool refreshLegacyPayerType = false)
    {
        using var before = ReadPreviousPayerObject(previousPayerInfoJson ?? "{}");
        using var after = JsonDocument.Parse(payerInfoJson ?? "{}");
        if (after.RootElement.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Payer details must be a JSON object.");

        var submitted = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in after.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind is JsonValueKind.String or JsonValueKind.Null)
                submitted[property.Name] = Trim(property.Value.GetString());
        }
        var fields = submitted.Where(pair => pair.Value != GetJson(before.RootElement, pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

        // The form can emit both names even when only one existed in the saved JSON.
        void ResolveAlias(string name, string alias, bool isPayerType = false)
        {
            fields.Remove(name);
            fields.Remove(alias);
            if (!submitted.TryGetValue(name, out var current) && !submitted.ContainsKey(alias)) return;
            current ??= submitted.GetValueOrDefault(alias);
            var previous = GetJson(before.RootElement, name) ?? GetJson(before.RootElement, alias);
            if (isPayerType)
            {
                current = IntakeProfilePrefill.NormalizeProviderType(current) ?? current;
                previous = IntakeProfilePrefill.NormalizeProviderType(previous) ?? previous;
            }
            // Unversioned chart clients may also resubmit an existing type to repair prefill.
            if (current != previous || (isPayerType && refreshLegacyPayerType && current is not null))
                fields[name] = current;
            submitted[name] = current;
        }
        ResolveAlias("secondaryMemberIdPolicyNumber", "secondaryMemberOrPolicyNumber");
        ResolveAlias("payerType", "providerType", isPayerType: true);
        if (previousPayerInfoJson is null && !fields.ContainsKey("authorizationNumber") && Trim(patient.AuthorizationNumber) is { } reference)
            fields["authorizationNumber"] = reference;

        await UpsertProviderAsync(db, patient, fields.GetValueOrDefault("primaryDoctorName"), fields.GetValueOrDefault("primaryDoctorPhone"), null, PatientProviderRole.PrimaryCare, userId, ct);
        await UpsertProviderAsync(db, patient, referringProviderName ?? fields.GetValueOrDefault("referringDoctorName"),
            fields.GetValueOrDefault("referringDoctorPhone"), referringProviderNpi ?? fields.GetValueOrDefault("referringDoctorNpi"), PatientProviderRole.Referring, userId, ct);

        var primary = await PatchLegacyPolicyAsync(db, patient, fields, InsuranceCoveragePriority.Primary, userId, ct, submitted);
        await PatchLegacyPolicyAsync(db, patient, fields, InsuranceCoveragePriority.Secondary, userId, ct);
        if (primary is not null)
            await PatchLegacyAuthorizationAsync(db, patient, primary, fields, before.RootElement, userId, ct);
        if (previousPayerInfoJson is null) await UpsertAuthorizationHistoryAsync(db, patient, payerInfoJson, userId, ct);
        else await UpdateAuthorizationHistoryAsync(db, patient, previousPayerInfoJson, payerInfoJson, userId, ct);
    }

    private static async Task<PatientInsurancePolicy?> PatchLegacyPolicyAsync(ApplicationDbContext db, Patient patient,
        Dictionary<string, string?> fields, InsuranceCoveragePriority priority, Guid userId, CancellationToken ct,
        IReadOnlyDictionary<string, string?>? primarySnapshot = null)
    {
        var policy = await db.PatientInsurancePolicies.SingleOrDefaultAsync(p => p.PatientId == patient.Id && p.ClinicId == patient.ClinicId
            && p.CoveragePriority == priority && !p.IsArchived && p.Status == InsurancePolicyStatus.Active, ct);
        if (primarySnapshot is not null)
        {
            if (fields.TryGetValue("coinsurancePercent", out var coinsurance) && LegacyAmount(coinsurance) > 100)
                throw new ArgumentException("Coinsurance percent must be between 0 and 100.");
            if (fields.ContainsKey("effectiveStartDate") || fields.ContainsKey("effectiveEndDate"))
            {
                var start = policy is null || fields.ContainsKey("effectiveStartDate")
                    ? LegacyDate(primarySnapshot.GetValueOrDefault("effectiveStartDate")) : policy.EffectiveStartDate;
                var end = policy is null || fields.ContainsKey("effectiveEndDate")
                    ? LegacyDate(primarySnapshot.GetValueOrDefault("effectiveEndDate")) : policy.EffectiveEndDate;
                if (start > end) throw new ArgumentException("Policy effective start date must not be after the end date.");
            }
        }
        var prefix = priority == InsuranceCoveragePriority.Secondary ? "secondary" : "";
        if (policy is null)
        {
            var payerTypeOnCreate = primarySnapshot?.GetValueOrDefault("payerType");
            if (fields.GetValueOrDefault(prefix + "insuranceCompanyName") is null && fields.GetValueOrDefault(prefix + "memberOrPolicyNumber") is null
                && fields.GetValueOrDefault(prefix + "memberIdPolicyNumber") is null && fields.GetValueOrDefault(prefix + "groupNumber") is null
                && ParsePayerType(payerTypeOnCreate ?? "") != InsurancePayerType.SelfPay)
                return null;
            policy = new() { PatientId = patient.Id, ClinicId = patient.ClinicId, CoveragePriority = priority, Status = InsurancePolicyStatus.Active };
            if (payerTypeOnCreate is not null) fields["payerType"] = payerTypeOnCreate;
            db.PatientInsurancePolicies.Add(policy);
        }
        bool touched = false;
        void Apply(string key, Action<string?> set)
        {
            if (!fields.TryGetValue(key, out var value)) return;
            set(value);
            touched = true;
        }
        Apply(prefix + "insuranceCompanyName", v =>
        {
            policy.CarrierDisplayName = v;
            policy.CarrierKey = v is null ? null : string.Join('-', v.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        });
        Apply(prefix + "memberOrPolicyNumber", v => policy.MemberOrPolicyNumber = v);
        Apply(prefix + "memberIdPolicyNumber", v => policy.MemberOrPolicyNumber = v);
        Apply(prefix + "groupNumber", v => policy.GroupNumber = v);
        if (priority == InsuranceCoveragePriority.Primary)
        {
            Apply("payerType", v => policy.PayerType = ParsePayerType(v ?? ""));
            Apply("adjusterName", v => policy.AdjusterName = v);
            Apply("adjusterPhone", v => policy.AdjusterPhone = v);
            Apply("adjusterEmail", v => policy.AdjusterEmail = v);
            Apply("adjusterFax", v => policy.AdjusterFax = v);
            Apply("totalVisitLimit", v => policy.TotalVisitLimit = LegacyAmount(v));
            Apply("visitLimitType", v => policy.VisitLimitType = v);
            Apply("yearType", v => policy.PlanYearType = ParseLegacyEnum<InsurancePlanYearType>(v));
            Apply("effectiveStartDate", v => policy.EffectiveStartDate = LegacyDate(v));
            Apply("effectiveEndDate", v => policy.EffectiveEndDate = LegacyDate(v));
            Apply("deductibleAmount", v => policy.DeductibleAmount = LegacyAmount(v));
            Apply("deductibleMet", v => policy.DeductibleMet = LegacyAmount(v));
            Apply("outOfPocketMaximum", v => policy.OutOfPocketMaximum = LegacyAmount(v));
            Apply("outOfPocketMet", v => policy.OutOfPocketMet = LegacyAmount(v));
            Apply("copayAmount", v => policy.CopayAmount = LegacyAmount(v));
            Apply("coinsurancePercent", v => policy.CoinsurancePercent = LegacyAmount(v));
        }
        if (touched)
        {
            policy.LastModifiedUtc = DateTime.UtcNow;
            policy.ModifiedByUserId = userId;
            policy.SyncState = SyncState.Pending;
        }
        return policy;
    }

    private static async Task PatchLegacyAuthorizationAsync(ApplicationDbContext db, Patient patient, PatientInsurancePolicy policy,
        Dictionary<string, string?> fields, JsonElement previous, Guid userId, CancellationToken ct)
    {
        // The legacy form also stores "active" and "lifetime", which have no
        // equivalent in the normalized enums. Keep these in the patient JSON.
        if (fields.TryGetValue("authorizationStatus", out var status) && string.Equals(status, "active", StringComparison.OrdinalIgnoreCase))
            fields.Remove("authorizationStatus");
        if (fields.TryGetValue("visitLimitPeriod", out var period) && string.Equals(period, "lifetime", StringComparison.OrdinalIgnoreCase))
            fields.Remove("visitLimitPeriod");
        string[] keys = ["authorizationNumber", "authorizationType", "authorizationStatus", "dateAuthorizationReceived",
            "authorizationStartDate", "authorizationEndDate", "numberOfVisitsOrUnitsAuthorized", "visitsUsed",
            "visitLimitPeriod", "reAuthorizationDueDate", "visitAlertThreshold", "authorizationNotes", "notesComments"];
        if (!keys.Any(fields.ContainsKey)) return;
        var reference = GetJson(previous, "authorizationNumber");
        var rows = await db.PatientInsuranceAuthorizations.Where(a => a.PatientInsurancePolicyId == policy.Id && a.PatientId == patient.Id
            && a.ClinicId == patient.ClinicId && !a.IsArchived && (reference == null || a.ReferenceNumber == reference)).ToListAsync(ct);
        if (rows.Count > 1 || (reference is not null && rows.Count == 0))
            throw new InvalidOperationException("Select the specific authorization or referral in the policy editor to correct this entry.");
        var row = rows.SingleOrDefault();
        if (row is null)
        {
            row = new() { PatientId = patient.Id, ClinicId = patient.ClinicId, PatientInsurancePolicyId = policy.Id };
            db.PatientInsuranceAuthorizations.Add(row);
        }
        void Apply(string key, Action<string?> set)
        {
            if (fields.TryGetValue(key, out var value)) set(value);
        }
        Apply("authorizationNumber", v => row.ReferenceNumber = v);
        Apply("authorizationType", v => row.AuthorizationType = v?.Contains("referral", StringComparison.OrdinalIgnoreCase) == true ? InsuranceAuthorizationType.Referral : InsuranceAuthorizationType.Authorization);
        Apply("authorizationStatus", v => row.Status = ParseLegacyEnum<InsuranceAuthorizationStatus>(v));
        Apply("dateAuthorizationReceived", v => row.ReceivedDate = LegacyDate(v));
        Apply("authorizationStartDate", v => row.StartDate = LegacyDate(v));
        Apply("authorizationEndDate", v => row.EndDate = LegacyDate(v));
        Apply("numberOfVisitsOrUnitsAuthorized", v => row.AuthorizedUnits = LegacyAmount(v));
        Apply("visitsUsed", v => row.UsedUnits = LegacyAmount(v));
        Apply("visitLimitPeriod", v => row.VisitLimitPeriod = ParseLegacyEnum<InsuranceVisitLimitPeriod>(v));
        Apply("reAuthorizationDueDate", v => row.ReauthorizationDueDate = LegacyDate(v));
        Apply("visitAlertThreshold", v => row.VisitAlertThreshold = v is null ? null : int.TryParse(v, out var n) && n >= 0 ? n : throw new ArgumentException("Enter a non-negative visit alert threshold."));
        Apply("authorizationNotes", v => row.Notes = v);
        Apply("notesComments", v => row.Notes = v);
        row.LastModifiedUtc = DateTime.UtcNow;
        row.ModifiedByUserId = userId;
        row.SyncState = SyncState.Pending;
    }

    private static T ParseLegacyEnum<T>(string? value) where T : struct, Enum =>
        value is null ? default : Enum.TryParse<T>(value.Replace("_", ""), true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed : throw new ArgumentException("Choose a supported value in the policy editor.");

    private static decimal? LegacyAmount(string? value) => value is null ? null :
        ParseDecimal(value) is { } amount && amount >= 0 ? amount : throw new ArgumentException("Enter a non-negative insurance amount.");

    private static DateTime? LegacyDate(string? value) => value is null ? null :
        ParseDate(value)?.ToUniversalTime() ?? throw new ArgumentException("Enter a valid insurance date.");

    public static async Task UpsertAsync(ApplicationDbContext db, Patient patient, IntakeResponseDraft draft, Guid userId, CancellationToken ct)
    {
        if (!await HasCurrentPolicySourcesAsync(db, patient.Id, patient.ClinicId, draft, ct))
            throw new InvalidOperationException(StalePolicySourceMessage);

        await UpsertCoreAsync(db, patient, draft, userId, ct, requireAcceptedSource: true);
    }

    private static async Task UpsertCoreAsync(ApplicationDbContext db, Patient patient, IntakeResponseDraft draft, Guid userId, CancellationToken ct, bool requireAcceptedSource)
    {
        await UpsertProviderAsync(db, patient, draft.PrimaryDoctorName, draft.PrimaryDoctorPhone, null, PatientProviderRole.PrimaryCare, userId, ct);
        await UpsertProviderAsync(db, patient, draft.ReferringDoctorName, draft.ReferringDoctorPhone, draft.ReferringDoctorNpi, PatientProviderRole.Referring, userId, ct);
        if (!draft.OmitPrimaryPolicy) await UpsertPolicyAsync(db, patient, InsuranceCoveragePriority.Primary, draft.InsuranceCompanyName, draft.MemberOrPolicyNumber, draft.GroupNumber, draft.PayerType, draft.AdjusterName, draft.AdjusterPhone, draft.AdjusterEmail, draft.AdjusterFax, userId, ct,
            draft.PrefilledPrimaryPolicyId, draft.PrefilledPrimaryPolicyModifiedUtc, requireAcceptedSource);
        if (!draft.OmitPrimaryPolicy && ParseDecimal(draft.TotalVisitLimit) is { } limit)
        {
            var primary = db.PatientInsurancePolicies.Local.SingleOrDefault(p => p.PatientId == patient.Id && p.ClinicId == patient.ClinicId
                && p.CoveragePriority == InsuranceCoveragePriority.Primary && !p.IsArchived && p.Status == InsurancePolicyStatus.Active);
            if (primary is not null) primary.TotalVisitLimit = limit;
        }
        if (!draft.OmitSecondaryPolicy) await UpsertPolicyAsync(db, patient, InsuranceCoveragePriority.Secondary, draft.SecondaryInsuranceCompanyName, draft.SecondaryMemberOrPolicyNumber, draft.SecondaryGroupNumber, null, null, null, null, null, userId, ct,
            draft.PrefilledSecondaryPolicyId, draft.PrefilledSecondaryPolicyModifiedUtc, requireAcceptedSource);
        if (!draft.OmitPrimaryPolicy) await UpsertAuthorizationAsync(db, patient, draft, userId, ct);
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

    private static async Task UpsertPolicyAsync(ApplicationDbContext db, Patient patient, InsuranceCoveragePriority priority, string? carrier, string? member, string? group, string? payerType, string? adjusterName, string? adjusterPhone, string? adjusterEmail, string? adjusterFax, Guid userId, CancellationToken ct,
        Guid? sourceId, DateTime? sourceModifiedUtc, bool requireAcceptedSource)
    {
        carrier = Trim(carrier); member = Trim(member); group = Trim(group); var normalizedPayerType = string.IsNullOrWhiteSpace(payerType) ? InsurancePayerType.Other : ParsePayerType(payerType); if (carrier is null && member is null && group is null && normalizedPayerType != InsurancePayerType.SelfPay) return;
        var policy = await db.PatientInsurancePolicies.FirstOrDefaultAsync(p => p.PatientId == patient.Id && p.ClinicId == patient.ClinicId
            && p.CoveragePriority == priority && !p.IsArchived && p.Status == InsurancePolicyStatus.Active
            && (!sourceId.HasValue || p.Id == sourceId.Value), ct);
        if ((requireAcceptedSource && !sourceId.HasValue && policy is not null)
            || (sourceId.HasValue && (policy is null || !IsPolicyVersionCurrent(policy, sourceModifiedUtc))))
            throw new InvalidOperationException(StalePolicySourceMessage);
        if (policy is null) { policy = new() { PatientId = patient.Id, ClinicId = patient.ClinicId, CoveragePriority = priority, Status = InsurancePolicyStatus.Active }; db.PatientInsurancePolicies.Add(policy); }
        if (carrier is not null) { policy.CarrierDisplayName = carrier; policy.CarrierKey = string.Join('-', carrier.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries)); }
        if (member is not null) policy.MemberOrPolicyNumber = member; if (group is not null) policy.GroupNumber = group;
        if (!string.IsNullOrWhiteSpace(payerType)) policy.PayerType = normalizedPayerType;
        if (Trim(adjusterName) is { } name) policy.AdjusterName = name; if (Trim(adjusterPhone) is { } phone) policy.AdjusterPhone = phone; if (Trim(adjusterEmail) is { } email) policy.AdjusterEmail = email; if (Trim(adjusterFax) is { } fax) policy.AdjusterFax = fax;
        policy.LastModifiedUtc = DateTime.UtcNow; policy.ModifiedByUserId = userId; policy.SyncState = SyncState.Pending;
    }

    private static async Task UpsertAuthorizationAsync(ApplicationDbContext db, Patient patient, IntakeResponseDraft draft, Guid userId, CancellationToken ct)
    {
        if (new[] { draft.AuthorizationNumber, draft.AuthorizationStatus, draft.DateAuthorizationReceived, draft.AuthorizationStartDate, draft.AuthorizationEndDate, draft.NumberOfVisitsOrUnitsAuthorized, draft.VisitsUsed, draft.ReAuthorizationDueDate, draft.VisitAlertThreshold, draft.AuthorizationNotes, draft.NotesComments }.All(string.IsNullOrWhiteSpace)) return;
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
        if (ParseDecimal(draft.NumberOfVisitsOrUnitsAuthorized) is { } authorized) authorization.AuthorizedUnits = authorized;
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
            if (!TryGetHistory(document.RootElement, out var history) || history.ValueKind != JsonValueKind.Array) return;
            var policy = db.PatientInsurancePolicies.Local.FirstOrDefault(value => value.PatientId == patient.Id && value.ClinicId == patient.ClinicId && value.CoveragePriority == InsuranceCoveragePriority.Primary && value.Status == InsurancePolicyStatus.Active && !value.IsArchived)
                ?? await db.PatientInsurancePolicies.FirstOrDefaultAsync(value => value.PatientId == patient.Id && value.ClinicId == patient.ClinicId && value.CoveragePriority == InsuranceCoveragePriority.Primary && value.Status == InsurancePolicyStatus.Active && !value.IsArchived, ct);
            if (policy is null) return;
            foreach (var item in history.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var recordType = GetJson(item, "recordType"); var reference = GetJson(item, "referenceNumber"); var start = ParseDate(GetJson(item, "startDate")); var end = ParseDate(GetJson(item, "endDate")); var type = recordType?.Contains("referral", StringComparison.OrdinalIgnoreCase) == true ? InsuranceAuthorizationType.Referral : InsuranceAuthorizationType.Authorization;
                var existing = db.PatientInsuranceAuthorizations.Local.FirstOrDefault(value => value.PatientInsurancePolicyId == policy.Id && value.ClinicId == patient.ClinicId && !value.IsArchived && value.AuthorizationType == type && value.ReferenceNumber == reference && value.StartDate == start && value.EndDate == end)
                    ?? await db.PatientInsuranceAuthorizations.FirstOrDefaultAsync(value => value.PatientInsurancePolicyId == policy.Id && value.ClinicId == patient.ClinicId && !value.IsArchived && value.AuthorizationType == type && value.ReferenceNumber == reference && value.StartDate == start && value.EndDate == end, ct);
                if (existing is null) { existing = new() { PatientInsurancePolicyId = policy.Id, PatientId = patient.Id, ClinicId = patient.ClinicId, AuthorizationType = type, ReferenceNumber = reference, StartDate = start, EndDate = end }; db.PatientInsuranceAuthorizations.Add(existing); }
                if (Enum.TryParse<InsuranceAuthorizationStatus>(GetJson(item, "status"), true, out var status)) existing.Status = status;
                if (ParseDecimal(GetJson(item, "visitsOrUnitsAuthorized")) is { } units) existing.AuthorizedUnits = units;
                if (Trim(GetJson(item, "notes")) is { } notes) existing.Notes = notes;
                existing.LastModifiedUtc = DateTime.UtcNow; existing.ModifiedByUserId = userId; existing.SyncState = SyncState.Pending;
            }
        }
    }

    private static JsonDocument ReadPreviousPayerObject(string previous)
    {
        try
        {
            var document = JsonDocument.Parse(previous);
            if (document.RootElement.ValueKind == JsonValueKind.Object) return document;
            document.Dispose();
        }
        catch (JsonException) { }
        return JsonDocument.Parse("{}");
    }

    private static async Task UpdateAuthorizationHistoryAsync(ApplicationDbContext db, Patient patient, string previous, string? current, Guid userId, CancellationToken ct)
    {
        using var before = ReadPreviousPayerObject(previous);
        using var after = JsonDocument.Parse(current ?? "{}");
        if (!TryGetHistory(after.RootElement, out var history) || history.ValueKind != JsonValueKind.Array) return;
        var oldRows = TryGetHistory(before.RootElement, out var oldHistory) && oldHistory.ValueKind == JsonValueKind.Array
            ? oldHistory.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.Object).ToList() : [];
        var policy = db.PatientInsurancePolicies.Local.FirstOrDefault(value => value.PatientId == patient.Id && value.ClinicId == patient.ClinicId && value.CoveragePriority == InsuranceCoveragePriority.Primary && value.Status == InsurancePolicyStatus.Active && !value.IsArchived)
            ?? await db.PatientInsurancePolicies.FirstOrDefaultAsync(value => value.PatientId == patient.Id && value.ClinicId == patient.ClinicId && value.CoveragePriority == InsuranceCoveragePriority.Primary && value.Status == InsurancePolicyStatus.Active && !value.IsArchived, ct);
        if (policy is null) return;
        foreach (var item in history.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.Object))
        {
            var key = GetJson(item, "entryId");
            var old = oldRows.FirstOrDefault(value => key is not null && GetJson(value, "entryId") == key);
            if (old.ValueKind == JsonValueKind.Undefined)
            {
                old = oldRows.FirstOrDefault(value => SameHistoryValues(value, item));
                if (old.ValueKind == JsonValueKind.Undefined && oldRows.Any(value => GetJson(value, "entryId") is null))
                    throw new InvalidOperationException("This older history entry has no stable identifier. Use the policy editor to correct its authorization or referral without duplicating history.");
            }
            if (old.ValueKind != JsonValueKind.Undefined && SameHistoryValues(old, item)) continue;
            var lookup = old.ValueKind == JsonValueKind.Undefined ? item : old;
            var type = GetJson(lookup, "recordType")?.Contains("referral", StringComparison.OrdinalIgnoreCase) == true ? InsuranceAuthorizationType.Referral : InsuranceAuthorizationType.Authorization;
            var reference = GetJson(lookup, "referenceNumber");
            var start = ParseDate(GetJson(lookup, "startDate"));
            var end = ParseDate(GetJson(lookup, "endDate"));
            var matches = await db.PatientInsuranceAuthorizations.Where(value => value.PatientId == patient.Id && value.PatientInsurancePolicyId == policy.Id
                && value.ClinicId == patient.ClinicId && !value.IsArchived && value.AuthorizationType == type && value.ReferenceNumber == reference && value.StartDate == start && value.EndDate == end).ToListAsync(ct);
            if (matches.Count > 1) throw new InvalidOperationException("This history entry matches multiple authorizations. Select the specific record in the policy editor.");
            var entity = matches.SingleOrDefault();
            if (entity is null && old.ValueKind != JsonValueKind.Undefined)
                throw new InvalidOperationException("This history entry has changed. Use the policy editor to correct the existing authorization or referral.");
            if (entity is not null && old.ValueKind == JsonValueKind.Undefined)
                throw new InvalidOperationException("This history entry matches a saved authorization or referral. Edit the existing record in the policy editor.");
            var isNew = entity is null;
            if (entity is null)
            {
                entity = new() { PatientInsurancePolicyId = policy.Id, PatientId = patient.Id, ClinicId = patient.ClinicId };
                db.PatientInsuranceAuthorizations.Add(entity);
            }
            bool Changed(string field) => isNew || old.ValueKind == JsonValueKind.Undefined || GetJson(old, field) != GetJson(item, field);
            if (Changed("recordType")) entity.AuthorizationType = GetJson(item, "recordType")?.Contains("referral", StringComparison.OrdinalIgnoreCase) == true ? InsuranceAuthorizationType.Referral : InsuranceAuthorizationType.Authorization;
            if (Changed("referenceNumber")) entity.ReferenceNumber = GetJson(item, "referenceNumber");
            if (Changed("startDate")) entity.StartDate = ParseDate(GetJson(item, "startDate"));
            if (Changed("endDate")) entity.EndDate = ParseDate(GetJson(item, "endDate"));
            if (Changed("status") && Enum.TryParse<InsuranceAuthorizationStatus>(GetJson(item, "status"), true, out var status)) entity.Status = status;
            if (Changed("visitsOrUnitsAuthorized")) entity.AuthorizedUnits = ParseDecimal(GetJson(item, "visitsOrUnitsAuthorized"));
            if (Changed("notes")) entity.Notes = GetJson(item, "notes");
            entity.LastModifiedUtc = DateTime.UtcNow;
            entity.ModifiedByUserId = userId;
            entity.SyncState = SyncState.Pending;
        }
    }

    private static bool TryGetHistory(JsonElement root, out JsonElement history)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (property.Name.Equals("authorizationReferralHistory", StringComparison.OrdinalIgnoreCase))
            {
                history = property.Value;
                return true;
            }
        }
        history = default;
        return false;
    }

    private static bool SameHistoryValues(JsonElement left, JsonElement right) =>
        new[] { "recordType", "referenceNumber", "status", "startDate", "endDate", "visitsOrUnitsAuthorized", "notes" }
            .All(field => GetJson(left, field) == GetJson(right, field));

    private static InsurancePayerType ParsePayerType(string value) => (IntakeProfilePrefill.NormalizeProviderType(value) ?? value).Trim().ToLowerInvariant() switch { "commercial" => InsurancePayerType.Commercial, "medicare" => InsurancePayerType.Medicare, "medicaid" => InsurancePayerType.Medicaid, "tricare" => InsurancePayerType.Tricare, "workers' compensation" => InsurancePayerType.WorkersCompensation, "motor vehicle" => InsurancePayerType.MotorVehicle, "liability" => InsurancePayerType.Liability, "self pay" => InsurancePayerType.SelfPay, _ => InsurancePayerType.Other };
    private static DateTime? ParseDate(string? value) => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed) ? parsed : null;
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

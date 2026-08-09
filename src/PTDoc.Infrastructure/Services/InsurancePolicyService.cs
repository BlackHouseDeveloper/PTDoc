using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Identity;
using PTDoc.Application.Insurance;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Infrastructure.Services;

public sealed class InsurancePolicyService(
    ApplicationDbContext db,
    ITenantContextAccessor tenantContext,
    IIdentityContextAccessor identityContext) : IInsurancePolicyService
{
    public Task<IReadOnlyList<InsurancePolicyDto>> ListAsync(Guid patientId, CancellationToken cancellationToken) =>
        ListAsync(patientId, includeArchived: false, cancellationToken: cancellationToken);

    public async Task<IReadOnlyList<InsurancePolicyDto>> ListAsync(Guid patientId, bool includeArchived, CancellationToken cancellationToken)
    {
        await RequirePatientAsync(patientId, cancellationToken);
        var query = db.PatientInsurancePolicies.AsNoTracking().Include(p => p.Authorizations)
            .Where(p => p.PatientId == patientId);
        if (!includeArchived)
        {
            query = query.Where(p => !p.IsArchived);
        }
        var rows = await query
            .OrderBy(p => p.CoveragePriority).ThenByDescending(p => p.EffectiveStartDate).ToListAsync(cancellationToken);
        return rows.Select(Map).ToList();
    }

    public async Task<InsurancePolicyDto> UpsertPolicyAsync(Guid patientId, Guid? policyId, UpsertInsurancePolicyRequest request, CancellationToken cancellationToken)
    {
        ValidatePolicy(request);
        var patient = await RequirePatientAsync(patientId, cancellationToken);
        var userId = identityContext.GetCurrentUserId();
        var policy = policyId.HasValue
            ? await db.PatientInsurancePolicies.Include(p => p.Authorizations).FirstOrDefaultAsync(p => p.Id == policyId.Value && p.PatientId == patientId, cancellationToken) ?? throw new KeyNotFoundException("Insurance policy not found.")
            : new PatientInsurancePolicy { PatientId = patientId, ClinicId = patient.ClinicId };
        if (request.ExpectedLastModifiedUtc.HasValue && policyId.HasValue && policy.LastModifiedUtc != request.ExpectedLastModifiedUtc.Value)
            throw new DbUpdateConcurrencyException("The insurance policy changed in another session.");
        if (request.Status == InsurancePolicyStatus.Active && await db.PatientInsurancePolicies.AnyAsync(p => p.PatientId == patientId && p.Id != policy.Id && !p.IsArchived && p.Status == InsurancePolicyStatus.Active && p.CoveragePriority == request.CoveragePriority, cancellationToken))
            throw new InvalidOperationException($"The patient already has an active {request.CoveragePriority} policy.");
        Apply(policy, request);
        Touch(policy, userId);
        if (!policyId.HasValue) db.PatientInsurancePolicies.Add(policy);
        await db.SaveChangesAsync(cancellationToken);
        return Map(policy);
    }

    public async Task ArchivePolicyAsync(Guid patientId, Guid policyId, CancellationToken cancellationToken)
    {
        var policy = await db.PatientInsurancePolicies.FirstOrDefaultAsync(p => p.Id == policyId && p.PatientId == patientId, cancellationToken) ?? throw new KeyNotFoundException("Insurance policy not found.");
        policy.IsArchived = true; policy.Status = InsurancePolicyStatus.Inactive; Touch(policy, identityContext.GetCurrentUserId());
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task ReorderAsync(Guid patientId, IReadOnlyDictionary<Guid, InsuranceCoveragePriority> priorities, CancellationToken cancellationToken)
    {
        await RequirePatientAsync(patientId, cancellationToken);
        if (priorities.Count != priorities.Values.Distinct().Count()) throw new InvalidOperationException("Coverage priorities must be unique.");
        var rows = await db.PatientInsurancePolicies.Where(p => p.PatientId == patientId && priorities.Keys.Contains(p.Id) && !p.IsArchived).ToListAsync(cancellationToken);
        if (rows.Count != priorities.Count) throw new KeyNotFoundException("One or more insurance policies were not found.");
        var occupiedByOtherPolicy = await db.PatientInsurancePolicies.AnyAsync(p => p.PatientId == patientId && !p.IsArchived && p.Status == InsurancePolicyStatus.Active && !priorities.Keys.Contains(p.Id) && priorities.Values.Contains(p.CoveragePriority), cancellationToken);
        if (occupiedByOtherPolicy) throw new InvalidOperationException("A requested coverage priority is assigned to another active policy.");
        var userId = identityContext.GetCurrentUserId();
        var originalStatuses = rows.ToDictionary(row => row.Id, row => row.Status);
        if (db.Database.IsRelational())
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            foreach (var row in rows.Where(row => row.Status == InsurancePolicyStatus.Active)) row.Status = InsurancePolicyStatus.Inactive;
            await db.SaveChangesAsync(cancellationToken);
            foreach (var row in rows) { row.CoveragePriority = priorities[row.Id]; row.Status = originalStatuses[row.Id]; Touch(row, userId); }
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        else
        {
            foreach (var row in rows) { row.CoveragePriority = priorities[row.Id]; Touch(row, userId); }
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<InsuranceAuthorizationDto> UpsertAuthorizationAsync(Guid patientId, Guid policyId, Guid? authorizationId, UpsertInsuranceAuthorizationRequest request, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(request.AuthorizationType)
            || !Enum.IsDefined(request.Status)
            || !Enum.IsDefined(request.VisitLimitPeriod))
            throw new ArgumentException("Authorization type, status, and visit-limit period must be supported values.");
        if (request.StartDate.HasValue && request.EndDate.HasValue && request.StartDate > request.EndDate) throw new ArgumentException("Authorization start date must not be after the end date.");
        if (request.AuthorizedUnits < 0 || request.UsedUnits < 0 || request.VisitAlertThreshold < 0)
            throw new ArgumentException("Authorization units and alert threshold cannot be negative.");
        var policy = await db.PatientInsurancePolicies.FirstOrDefaultAsync(p => p.Id == policyId && p.PatientId == patientId && !p.IsArchived, cancellationToken) ?? throw new KeyNotFoundException("Insurance policy not found.");
        var authorization = authorizationId.HasValue
            ? await db.PatientInsuranceAuthorizations.FirstOrDefaultAsync(a => a.Id == authorizationId.Value && a.PatientInsurancePolicyId == policyId && a.PatientId == patientId, cancellationToken) ?? throw new KeyNotFoundException("Insurance authorization not found.")
            : new PatientInsuranceAuthorization { PatientId = patientId, PatientInsurancePolicyId = policyId, ClinicId = policy.ClinicId };
        if (request.ExpectedLastModifiedUtc.HasValue && authorizationId.HasValue && authorization.LastModifiedUtc != request.ExpectedLastModifiedUtc.Value)
            throw new DbUpdateConcurrencyException("The insurance authorization changed in another session.");
        authorization.AuthorizationType = request.AuthorizationType; authorization.ReferenceNumber = Trim(request.ReferenceNumber); authorization.Status = request.Status;
        authorization.ReceivedDate = request.ReceivedDate; authorization.StartDate = request.StartDate; authorization.EndDate = request.EndDate;
        authorization.AuthorizedUnits = request.AuthorizedUnits; authorization.UsedUnits = request.UsedUnits; authorization.VisitLimitPeriod = request.VisitLimitPeriod;
        authorization.ReauthorizationDueDate = request.ReauthorizationDueDate; authorization.VisitAlertThreshold = request.VisitAlertThreshold; authorization.Notes = Trim(request.Notes);
        authorization.IsArchived = false; authorization.LastModifiedUtc = DateTime.UtcNow; authorization.ModifiedByUserId = identityContext.GetCurrentUserId(); authorization.SyncState = SyncState.Pending;
        if (!authorizationId.HasValue) db.PatientInsuranceAuthorizations.Add(authorization);
        await db.SaveChangesAsync(cancellationToken);
        return Map(authorization);
    }

    public async Task ArchiveAuthorizationAsync(Guid patientId, Guid policyId, Guid authorizationId, CancellationToken cancellationToken)
    {
        var row = await db.PatientInsuranceAuthorizations.FirstOrDefaultAsync(a => a.Id == authorizationId && a.PatientInsurancePolicyId == policyId && a.PatientId == patientId, cancellationToken) ?? throw new KeyNotFoundException("Insurance authorization not found.");
        row.IsArchived = true; row.LastModifiedUtc = DateTime.UtcNow; row.ModifiedByUserId = identityContext.GetCurrentUserId(); row.SyncState = SyncState.Pending;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<PayerBackfillReport> BackfillLegacyPayerDataAsync(CancellationToken cancellationToken)
    {
        _ = tenantContext.GetCurrentClinicId() ?? throw new InvalidOperationException("Legacy payer backfill must run within an explicit clinic scope.");
        var patients = await db.Patients.Where(p => p.PayerInfoJson != "{}" && p.PayerInfoJson != string.Empty).ToListAsync(cancellationToken);
        var policyCount = 0; var authCount = 0; var providerCount = 0; var ambiguous = new List<Guid>(); var malformed = new List<Guid>();
        foreach (var patient in patients)
        {
            try
            {
                using var document = JsonDocument.Parse(patient.PayerInfoJson);
                if (document.RootElement.ValueKind != JsonValueKind.Object) { malformed.Add(patient.Id); continue; }
                var root = document.RootElement;
                policyCount += await ImportPolicyAsync(patient, root, InsuranceCoveragePriority.Primary, "insuranceCompanyName", "memberIdPolicyNumber", "groupNumber", cancellationToken);
                policyCount += await ImportPolicyAsync(patient, root, InsuranceCoveragePriority.Secondary, "secondaryInsuranceCompanyName", "secondaryMemberIdPolicyNumber", "secondaryGroupNumber", cancellationToken);
                var primary = await db.PatientInsurancePolicies.FirstOrDefaultAsync(p => p.PatientId == patient.Id && p.CoveragePriority == InsuranceCoveragePriority.Primary && !p.IsArchived && p.Status == InsurancePolicyStatus.Active, cancellationToken);
                if (primary is not null && !await db.PatientInsuranceAuthorizations.AnyAsync(a => a.PatientInsurancePolicyId == primary.Id && !a.IsArchived, cancellationToken) && HasAny(root, "authorizationNumber", "authorizationStartDate", "authorizationEndDate", "numberOfVisitsOrUnitsAuthorized"))
                {
                    db.PatientInsuranceAuthorizations.Add(new PatientInsuranceAuthorization { PatientInsurancePolicyId = primary.Id, PatientId = patient.Id, ClinicId = patient.ClinicId, AuthorizationType = InsuranceAuthorizationType.Authorization, ReferenceNumber = Get(root, "authorizationNumber"), Status = ParseAuthorizationStatus(Get(root, "authorizationStatus")), ReceivedDate = Date(Get(root, "dateAuthorizationReceived")), StartDate = Date(Get(root, "authorizationStartDate")), EndDate = Date(Get(root, "authorizationEndDate")), AuthorizedUnits = Decimal(Get(root, "numberOfVisitsOrUnitsAuthorized")) ?? Decimal(Get(root, "totalVisitLimit")), UsedUnits = Decimal(Get(root, "visitsUsed")), ReauthorizationDueDate = Date(Get(root, "reAuthorizationDueDate")), VisitAlertThreshold = Int(Get(root, "visitAlertThreshold")), Notes = Get(root, "notesComments"), LastModifiedUtc = DateTime.UtcNow, ModifiedByUserId = identityContext.GetCurrentUserId(), SyncState = SyncState.Pending });
                    authCount++;
                }
                if (primary is not null) authCount += await ImportAuthorizationHistoryAsync(patient, primary, root, cancellationToken);
                var referring = Get(root, "referringPhysicianName") ?? patient.ReferringPhysician;
                var npi = Get(root, "physicianNpiNumber") ?? patient.PhysicianNpi;
                if (!string.IsNullOrWhiteSpace(referring) && !await db.PatientProviderRelationships.AnyAsync(r => r.PatientId == patient.Id && r.Role == PatientProviderRole.Referring && !r.IsArchived, cancellationToken))
                {
                    var matches = npi is null ? [] : await db.ProviderDirectoryEntries.Where(e => e.Npi == npi && !e.IsArchived && e.Status == ProviderDirectoryStatus.Active).ToListAsync(cancellationToken);
                    if (matches.Count > 1) ambiguous.Add(patient.Id);
                    else
                    {
                        var provider = matches.SingleOrDefault() ?? CreateLegacyProvider(patient, referring!, npi);
                        if (matches.Count == 0) { db.ProviderDirectoryEntries.Add(provider); providerCount++; }
                        db.PatientProviderRelationships.Add(new PatientProviderRelationship { PatientId = patient.Id, ProviderDirectoryEntryId = provider.Id, ClinicId = patient.ClinicId, Role = PatientProviderRole.Referring, IsPrimary = true, LastModifiedUtc = DateTime.UtcNow, ModifiedByUserId = identityContext.GetCurrentUserId(), SyncState = SyncState.Pending });
                    }
                }
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (JsonException) { malformed.Add(patient.Id); }
        }
        return new(patients.Count, policyCount, authCount, providerCount, ambiguous, malformed);
    }

    private async Task<int> ImportPolicyAsync(Patient patient, JsonElement root, InsuranceCoveragePriority priority, string carrierKey, string memberKey, string groupKey, CancellationToken ct)
    {
        var carrier = Get(root, carrierKey); var member = Get(root, memberKey) ?? (priority == InsuranceCoveragePriority.Primary ? Get(root, "memberOrPolicyNumber") : Get(root, "secondaryMemberOrPolicyNumber")); var group = Get(root, groupKey);
        if (string.IsNullOrWhiteSpace(carrier) && string.IsNullOrWhiteSpace(member) && string.IsNullOrWhiteSpace(group)) return 0;
        if (await db.PatientInsurancePolicies.AnyAsync(p => p.PatientId == patient.Id && p.CoveragePriority == priority && !p.IsArchived && p.Status == InsurancePolicyStatus.Active, ct)) return 0;
        db.PatientInsurancePolicies.Add(new PatientInsurancePolicy { PatientId = patient.Id, ClinicId = patient.ClinicId, CoveragePriority = priority, CarrierKey = Slug(carrier), CarrierDisplayName = carrier, PayerType = ParsePayerType(Get(root, "providerType") ?? Get(root, "payerType")), MemberOrPolicyNumber = member, GroupNumber = group, EffectiveStartDate = Date(Get(root, "effectiveStartDate")), EffectiveEndDate = Date(Get(root, "effectiveEndDate")), PlanYearType = ParsePlanYear(Get(root, "yearType")), DeductibleAmount = Decimal(Get(root, "deductibleAmount")), DeductibleMet = Decimal(Get(root, "deductibleMet")), OutOfPocketMaximum = Decimal(Get(root, "outOfPocketMaximum")), OutOfPocketMet = Decimal(Get(root, "outOfPocketMet")), CopayAmount = Decimal(Get(root, "copayAmount")), CoinsurancePercent = Decimal(Get(root, "coinsurancePercent")), AdjusterName = Get(root, "adjusterName"), AdjusterPhone = Get(root, "adjusterPhone"), AdjusterEmail = Get(root, "adjusterEmail"), AdjusterFax = Get(root, "adjusterFax"), Status = InsurancePolicyStatus.Active, LastModifiedUtc = DateTime.UtcNow, ModifiedByUserId = identityContext.GetCurrentUserId(), SyncState = SyncState.Pending });
        await db.SaveChangesAsync(ct); return 1;
    }

    private async Task<int> ImportAuthorizationHistoryAsync(Patient patient, PatientInsurancePolicy policy, JsonElement root, CancellationToken ct)
    {
        if (!root.TryGetProperty("authorizationReferralHistory", out var history) || history.ValueKind != JsonValueKind.Array) return 0;
        var created = 0;
        foreach (var item in history.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var recordType = Get(item, "recordType"); var reference = Get(item, "referenceNumber"); var start = Date(Get(item, "startDate")); var end = Date(Get(item, "endDate"));
            var type = recordType?.Contains("referral", StringComparison.OrdinalIgnoreCase) == true ? InsuranceAuthorizationType.Referral : InsuranceAuthorizationType.Authorization;
            var alreadyTracked = db.PatientInsuranceAuthorizations.Local.Any(value => value.PatientInsurancePolicyId == policy.Id && !value.IsArchived && value.AuthorizationType == type && value.ReferenceNumber == reference && value.StartDate == start && value.EndDate == end);
            if (alreadyTracked || await db.PatientInsuranceAuthorizations.AnyAsync(value => value.PatientInsurancePolicyId == policy.Id && !value.IsArchived && value.AuthorizationType == type && value.ReferenceNumber == reference && value.StartDate == start && value.EndDate == end, ct)) continue;
            db.PatientInsuranceAuthorizations.Add(new() { PatientInsurancePolicyId = policy.Id, PatientId = patient.Id, ClinicId = patient.ClinicId, AuthorizationType = type, ReferenceNumber = reference, Status = ParseAuthorizationStatus(Get(item, "status")), StartDate = start, EndDate = end, AuthorizedUnits = Decimal(Get(item, "visitsOrUnitsAuthorized")), Notes = Get(item, "notes"), LastModifiedUtc = DateTime.UtcNow, ModifiedByUserId = identityContext.GetCurrentUserId(), SyncState = SyncState.Pending }); created++;
        }
        return created;
    }

    private ProviderDirectoryEntry CreateLegacyProvider(Patient patient, string display, string? npi) { var parts = display.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries); return new ProviderDirectoryEntry { ClinicId = patient.ClinicId, FirstName = parts.Length > 1 ? string.Join(' ', parts[..^1]) : string.Empty, LastName = parts[^1], Npi = npi, Status = ProviderDirectoryStatus.Pending, SubmissionSource = ProviderSubmissionSource.LegacyMigration, SubmittedAtUtc = DateTime.UtcNow, LastModifiedUtc = DateTime.UtcNow, ModifiedByUserId = identityContext.GetCurrentUserId(), SyncState = SyncState.Pending }; }
    private async Task<Patient> RequirePatientAsync(Guid id, CancellationToken ct) => await db.Patients.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new KeyNotFoundException("Patient not found.");
    private static void ValidatePolicy(UpsertInsurancePolicyRequest request)
    {
        if (!Enum.IsDefined(request.CoveragePriority)
            || !Enum.IsDefined(request.PayerType)
            || !Enum.IsDefined(request.PlanYearType)
            || !Enum.IsDefined(request.Status))
            throw new ArgumentException("Coverage priority, payer type, plan year, and policy status must be supported values.");
        if (request.EffectiveStartDate.HasValue
            && request.EffectiveEndDate.HasValue
            && request.EffectiveStartDate > request.EffectiveEndDate)
            throw new ArgumentException("Policy effective start date must not be after the end date.");
        if (request.PayerType != InsurancePayerType.SelfPay && string.IsNullOrWhiteSpace(request.CarrierDisplayName))
            throw new ArgumentException("Carrier name is required unless the payer type is Self Pay.");
        if (new[]
            {
                request.DeductibleAmount,
                request.DeductibleMet,
                request.OutOfPocketMaximum,
                request.OutOfPocketMet,
                request.CopayAmount
            }.Any(value => value < 0)
            || request.CoinsurancePercent < 0
            || request.CoinsurancePercent > 100)
            throw new ArgumentException("Insurance cost-sharing values must be within their valid ranges.");
    }
    private static void Apply(PatientInsurancePolicy p, UpsertInsurancePolicyRequest r) { p.CoveragePriority = r.CoveragePriority; p.CarrierKey = Trim(r.CarrierKey); p.CarrierDisplayName = Trim(r.CarrierDisplayName); p.PayerType = r.PayerType; p.MemberOrPolicyNumber = Trim(r.MemberOrPolicyNumber); p.GroupNumber = Trim(r.GroupNumber); p.EffectiveStartDate = r.EffectiveStartDate; p.EffectiveEndDate = r.EffectiveEndDate; p.PlanYearType = r.PlanYearType; p.DeductibleAmount = r.DeductibleAmount; p.DeductibleMet = r.DeductibleMet; p.OutOfPocketMaximum = r.OutOfPocketMaximum; p.OutOfPocketMet = r.OutOfPocketMet; p.CopayAmount = r.CopayAmount; p.CoinsurancePercent = r.CoinsurancePercent; p.AdjusterName = Trim(r.AdjusterName); p.AdjusterPhone = Trim(r.AdjusterPhone); p.AdjusterEmail = Trim(r.AdjusterEmail); p.AdjusterFax = Trim(r.AdjusterFax); p.Status = r.Status; p.IsArchived = false; }
    private static void Touch(PatientInsurancePolicy p, Guid user) { p.LastModifiedUtc = DateTime.UtcNow; p.ModifiedByUserId = user; p.SyncState = SyncState.Pending; }
    private static InsurancePolicyDto Map(PatientInsurancePolicy p) => new() { Id = p.Id, PatientId = p.PatientId, CoveragePriority = p.CoveragePriority, CarrierKey = p.CarrierKey, CarrierDisplayName = p.CarrierDisplayName, PayerType = p.PayerType, MemberOrPolicyNumber = p.MemberOrPolicyNumber, GroupNumber = p.GroupNumber, EffectiveStartDate = p.EffectiveStartDate, EffectiveEndDate = p.EffectiveEndDate, PlanYearType = p.PlanYearType, DeductibleAmount = p.DeductibleAmount, DeductibleMet = p.DeductibleMet, OutOfPocketMaximum = p.OutOfPocketMaximum, OutOfPocketMet = p.OutOfPocketMet, CopayAmount = p.CopayAmount, CoinsurancePercent = p.CoinsurancePercent, AdjusterName = p.AdjusterName, AdjusterPhone = p.AdjusterPhone, AdjusterEmail = p.AdjusterEmail, AdjusterFax = p.AdjusterFax, Status = p.Status, IsArchived = p.IsArchived, LastModifiedUtc = p.LastModifiedUtc, Authorizations = p.Authorizations.Where(a => !a.IsArchived).Select(Map).ToList() };
    private static InsuranceAuthorizationDto Map(PatientInsuranceAuthorization a) => new() { Id = a.Id, PolicyId = a.PatientInsurancePolicyId, AuthorizationType = a.AuthorizationType, ReferenceNumber = a.ReferenceNumber, Status = a.Status, ReceivedDate = a.ReceivedDate, StartDate = a.StartDate, EndDate = a.EndDate, AuthorizedUnits = a.AuthorizedUnits, UsedUnits = a.UsedUnits, VisitLimitPeriod = a.VisitLimitPeriod, ReauthorizationDueDate = a.ReauthorizationDueDate, VisitAlertThreshold = a.VisitAlertThreshold, Notes = a.Notes, LastModifiedUtc = a.LastModifiedUtc };
    private static string? Trim(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim(); private static string? Get(JsonElement r, string n) => r.TryGetProperty(n, out var e) && e.ValueKind == JsonValueKind.String ? Trim(e.GetString()) : null; private static bool HasAny(JsonElement r, params string[] n) => n.Any(x => !string.IsNullOrWhiteSpace(Get(r, x))); private static DateTime? Date(string? v) => DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var x) ? x : null; private static decimal? Decimal(string? v) => decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var x) ? x : null; private static int? Int(string? v) => int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var x) ? x : null; private static string? Slug(string? v) => string.IsNullOrWhiteSpace(v) ? null : string.Join('-', v.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    private static InsurancePayerType ParsePayerType(string? v) => v?.Trim().ToLowerInvariant() switch { "medicare" => InsurancePayerType.Medicare, "medicaid" => InsurancePayerType.Medicaid, "workers' compensation" or "workers compensation" or "workers_comp" => InsurancePayerType.WorkersCompensation, "motor vehicle" or "motor_vehicle" => InsurancePayerType.MotorVehicle, "liability" => InsurancePayerType.Liability, "self pay" or "self_pay" => InsurancePayerType.SelfPay, "commercial" => InsurancePayerType.Commercial, _ => InsurancePayerType.Other };
    private static InsurancePlanYearType ParsePlanYear(string? v) => v?.Contains("calendar", StringComparison.OrdinalIgnoreCase) == true ? InsurancePlanYearType.CalendarYear : v?.Contains("plan", StringComparison.OrdinalIgnoreCase) == true ? InsurancePlanYearType.PlanYear : InsurancePlanYearType.Unspecified;
    private static InsuranceAuthorizationStatus ParseAuthorizationStatus(string? v) => Enum.TryParse<InsuranceAuthorizationStatus>(v, true, out var s) ? s : InsuranceAuthorizationStatus.Unknown;
}

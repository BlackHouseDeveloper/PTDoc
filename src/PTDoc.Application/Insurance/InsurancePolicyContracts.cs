using System.ComponentModel.DataAnnotations;
using PTDoc.Core.Models;

namespace PTDoc.Application.Insurance;

public sealed class InsurancePolicyDto
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public InsuranceCoveragePriority CoveragePriority { get; set; }
    public string? CarrierKey { get; set; }
    public string? CarrierDisplayName { get; set; }
    public InsurancePayerType PayerType { get; set; }
    public string? MemberOrPolicyNumber { get; set; }
    public string? GroupNumber { get; set; }
    public DateTime? EffectiveStartDate { get; set; }
    public DateTime? EffectiveEndDate { get; set; }
    public InsurancePlanYearType PlanYearType { get; set; }
    public decimal? DeductibleAmount { get; set; }
    public decimal? DeductibleMet { get; set; }
    public decimal? OutOfPocketMaximum { get; set; }
    public decimal? OutOfPocketMet { get; set; }
    public decimal? CopayAmount { get; set; }
    public decimal? CoinsurancePercent { get; set; }
    public string? AdjusterName { get; set; }
    public string? AdjusterPhone { get; set; }
    public string? AdjusterEmail { get; set; }
    public string? AdjusterFax { get; set; }
    public InsurancePolicyStatus Status { get; set; }
    public DateTime LastModifiedUtc { get; set; }
    public IReadOnlyList<InsuranceAuthorizationDto> Authorizations { get; set; } = [];
}

public sealed class UpsertInsurancePolicyRequest
{
    public InsuranceCoveragePriority CoveragePriority { get; set; }
    [MaxLength(100)] public string? CarrierKey { get; set; }
    [MaxLength(200)] public string? CarrierDisplayName { get; set; }
    public InsurancePayerType PayerType { get; set; }
    [MaxLength(100)] public string? MemberOrPolicyNumber { get; set; }
    [MaxLength(100)] public string? GroupNumber { get; set; }
    public DateTime? EffectiveStartDate { get; set; }
    public DateTime? EffectiveEndDate { get; set; }
    public InsurancePlanYearType PlanYearType { get; set; }
    [Range(0, double.MaxValue)] public decimal? DeductibleAmount { get; set; }
    [Range(0, double.MaxValue)] public decimal? DeductibleMet { get; set; }
    [Range(0, double.MaxValue)] public decimal? OutOfPocketMaximum { get; set; }
    [Range(0, double.MaxValue)] public decimal? OutOfPocketMet { get; set; }
    [Range(0, double.MaxValue)] public decimal? CopayAmount { get; set; }
    [Range(0, 100)] public decimal? CoinsurancePercent { get; set; }
    [MaxLength(150)] public string? AdjusterName { get; set; }
    [MaxLength(30)] public string? AdjusterPhone { get; set; }
    [EmailAddress, MaxLength(255)] public string? AdjusterEmail { get; set; }
    [MaxLength(30)] public string? AdjusterFax { get; set; }
    public InsurancePolicyStatus Status { get; set; } = InsurancePolicyStatus.Active;
    public DateTime? ExpectedLastModifiedUtc { get; set; }
}

public sealed class InsuranceAuthorizationDto
{
    public Guid Id { get; set; }
    public Guid PolicyId { get; set; }
    public InsuranceAuthorizationType AuthorizationType { get; set; }
    public string? ReferenceNumber { get; set; }
    public InsuranceAuthorizationStatus Status { get; set; }
    public DateTime? ReceivedDate { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public decimal? AuthorizedUnits { get; set; }
    public decimal? UsedUnits { get; set; }
    public InsuranceVisitLimitPeriod VisitLimitPeriod { get; set; }
    public DateTime? ReauthorizationDueDate { get; set; }
    public int? VisitAlertThreshold { get; set; }
    public string? Notes { get; set; }
    public DateTime LastModifiedUtc { get; set; }
}

public sealed class UpsertInsuranceAuthorizationRequest
{
    public InsuranceAuthorizationType AuthorizationType { get; set; }
    [MaxLength(100)] public string? ReferenceNumber { get; set; }
    public InsuranceAuthorizationStatus Status { get; set; }
    public DateTime? ReceivedDate { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    [Range(0, double.MaxValue)] public decimal? AuthorizedUnits { get; set; }
    [Range(0, double.MaxValue)] public decimal? UsedUnits { get; set; }
    public InsuranceVisitLimitPeriod VisitLimitPeriod { get; set; }
    public DateTime? ReauthorizationDueDate { get; set; }
    [Range(0, int.MaxValue)] public int? VisitAlertThreshold { get; set; }
    [MaxLength(2000)] public string? Notes { get; set; }
    public DateTime? ExpectedLastModifiedUtc { get; set; }
}

public sealed record PayerBackfillReport(int PatientsScanned, int PoliciesCreated, int AuthorizationsCreated, int ProviderCandidatesCreated, IReadOnlyList<Guid> AmbiguousPatientIds, IReadOnlyList<Guid> MalformedPatientIds);

public interface IInsurancePolicyService
{
    Task<IReadOnlyList<InsurancePolicyDto>> ListAsync(Guid patientId, CancellationToken cancellationToken);
    Task<InsurancePolicyDto> UpsertPolicyAsync(Guid patientId, Guid? policyId, UpsertInsurancePolicyRequest request, CancellationToken cancellationToken);
    Task ArchivePolicyAsync(Guid patientId, Guid policyId, CancellationToken cancellationToken);
    Task ReorderAsync(Guid patientId, IReadOnlyDictionary<Guid, InsuranceCoveragePriority> priorities, CancellationToken cancellationToken);
    Task<InsuranceAuthorizationDto> UpsertAuthorizationAsync(Guid patientId, Guid policyId, Guid? authorizationId, UpsertInsuranceAuthorizationRequest request, CancellationToken cancellationToken);
    Task ArchiveAuthorizationAsync(Guid patientId, Guid policyId, Guid authorizationId, CancellationToken cancellationToken);
    Task<PayerBackfillReport> BackfillLegacyPayerDataAsync(CancellationToken cancellationToken);
}

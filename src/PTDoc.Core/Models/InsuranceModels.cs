namespace PTDoc.Core.Models;

public sealed class PatientInsurancePolicy : ISyncTrackedEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PatientId { get; set; }
    public Guid? ClinicId { get; set; }
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
    public InsurancePolicyStatus Status { get; set; } = InsurancePolicyStatus.Active;
    public bool IsArchived { get; set; }
    public DateTime LastModifiedUtc { get; set; }
    public Guid ModifiedByUserId { get; set; }
    public SyncState SyncState { get; set; }

    public Patient? Patient { get; set; }
    public Clinic? Clinic { get; set; }
    public ICollection<PatientInsuranceAuthorization> Authorizations { get; set; } = new List<PatientInsuranceAuthorization>();
}

public sealed class PatientInsuranceAuthorization : ISyncTrackedEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PatientInsurancePolicyId { get; set; }
    public Guid PatientId { get; set; }
    public Guid? ClinicId { get; set; }
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
    public bool IsArchived { get; set; }
    public DateTime LastModifiedUtc { get; set; }
    public Guid ModifiedByUserId { get; set; }
    public SyncState SyncState { get; set; }

    public PatientInsurancePolicy? Policy { get; set; }
    public Patient? Patient { get; set; }
    public Clinic? Clinic { get; set; }
}

public enum InsuranceCoveragePriority { Primary = 0, Secondary = 1, Tertiary = 2 }
public enum InsurancePayerType { Commercial = 0, Medicare = 1, Medicaid = 2, WorkersCompensation = 3, MotorVehicle = 4, Liability = 5, SelfPay = 6, Other = 7 }
public enum InsurancePlanYearType { Unspecified = 0, CalendarYear = 1, PlanYear = 2 }
public enum InsurancePolicyStatus { Active = 0, Inactive = 1, Expired = 2 }
public enum InsuranceAuthorizationType { Authorization = 0, Referral = 1 }
public enum InsuranceAuthorizationStatus { Unknown = 0, Pending = 1, Approved = 2, Denied = 3, Expired = 4 }
public enum InsuranceVisitLimitPeriod { Unspecified = 0, AuthorizationPeriod = 1, CalendarYear = 2, PlanYear = 3, Episode = 4 }

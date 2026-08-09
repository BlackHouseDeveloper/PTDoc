using System.ComponentModel.DataAnnotations;
using PTDoc.Core.Models;

namespace PTDoc.Application.Providers;

public sealed class ProviderDirectoryEntryDto
{
    public Guid Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Credentials { get; set; }
    public string? Npi { get; set; }
    public string? Specialty { get; set; }
    public string? TaxonomyCode { get; set; }
    public string? OrganizationName { get; set; }
    public string? Phone { get; set; }
    public string? Fax { get; set; }
    public string? Email { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? ZipCode { get; set; }
    public ProviderDirectoryStatus Status { get; set; }
    public ProviderSubmissionSource SubmissionSource { get; set; }
    public DateTime LastModifiedUtc { get; set; }
    public IReadOnlyList<ProviderDuplicateCandidateDto> PossibleDuplicates { get; set; } = [];
}

public sealed class ProviderDuplicateCandidateDto
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? Npi { get; set; }
    public string Reason { get; set; } = string.Empty;
    public ProviderDirectoryStatus Status { get; set; }
}

public class SubmitProviderCandidateRequest
{
    [Required, MaxLength(100)] public string FirstName { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string LastName { get; set; } = string.Empty;
    [MaxLength(50)] public string? Credentials { get; set; }
    [RegularExpression("^[0-9]{10}$")] public string? Npi { get; set; }
    [MaxLength(150)] public string? Specialty { get; set; }
    [MaxLength(20)] public string? TaxonomyCode { get; set; }
    [MaxLength(200)] public string? OrganizationName { get; set; }
    [MaxLength(30)] public string? Phone { get; set; }
    [MaxLength(30)] public string? Fax { get; set; }
    [EmailAddress, MaxLength(255)] public string? Email { get; set; }
    [MaxLength(200)] public string? AddressLine1 { get; set; }
    [MaxLength(200)] public string? AddressLine2 { get; set; }
    [MaxLength(100)] public string? City { get; set; }
    [MaxLength(100)] public string? State { get; set; }
    [MaxLength(20)] public string? ZipCode { get; set; }
    public ProviderSubmissionSource SubmissionSource { get; set; } = ProviderSubmissionSource.Staff;
    public Guid? PatientId { get; set; }
    public PatientProviderRole? PatientRole { get; set; }
}

public sealed class UpdateProviderCandidateRequest : SubmitProviderCandidateRequest
{
    public DateTime ExpectedLastModifiedUtc { get; set; }
}

public sealed class ProviderDecisionRequest
{
    [MaxLength(500)] public string? Reason { get; set; }
    public Guid? MergeIntoProviderId { get; set; }
    public bool ConfirmDuplicate { get; set; }
}

public sealed class PatientProviderRelationshipDto
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public Guid ProviderId { get; set; }
    public PatientProviderRole Role { get; set; }
    public bool IsPrimary { get; set; }
    public DateTime? EffectiveStartDate { get; set; }
    public DateTime? EffectiveEndDate { get; set; }
    public bool IsArchived { get; set; }
    public ProviderDirectoryEntryDto Provider { get; set; } = new();
    public DateTime LastModifiedUtc { get; set; }
}

public sealed class UpsertPatientProviderRelationshipRequest
{
    public Guid ProviderId { get; set; }
    public PatientProviderRole Role { get; set; }
    public bool IsPrimary { get; set; }
    public DateTime? EffectiveStartDate { get; set; }
    public DateTime? EffectiveEndDate { get; set; }
    public DateTime? ExpectedLastModifiedUtc { get; set; }
}

public interface IProviderDirectoryService
{
    Task<IReadOnlyList<ProviderDirectoryEntryDto>> SearchAsync(string? query, ProviderDirectoryStatus? status, int take, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProviderDirectoryEntryDto>> SearchForAdministrationAsync(string? query, ProviderDirectoryStatus? status, int take, CancellationToken cancellationToken);
    Task<ProviderDirectoryEntryDto?> GetAsync(Guid providerId, CancellationToken cancellationToken);
    Task<ProviderDirectoryEntryDto> SubmitAsync(SubmitProviderCandidateRequest request, CancellationToken cancellationToken);
    Task<ProviderDirectoryEntryDto> UpdateAsync(Guid providerId, UpdateProviderCandidateRequest request, CancellationToken cancellationToken);
    Task<ProviderDirectoryEntryDto> ApproveAsync(Guid providerId, ProviderDecisionRequest request, CancellationToken cancellationToken);
    Task<ProviderDirectoryEntryDto> RejectAsync(Guid providerId, ProviderDecisionRequest request, CancellationToken cancellationToken);
    Task ArchiveAsync(Guid providerId, ProviderDecisionRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<PatientProviderRelationshipDto>> ListPatientRelationshipsAsync(Guid patientId, CancellationToken cancellationToken);
    Task<PatientProviderRelationshipDto> UpsertPatientRelationshipAsync(Guid patientId, Guid? relationshipId, UpsertPatientProviderRelationshipRequest request, CancellationToken cancellationToken);
    Task ArchivePatientRelationshipAsync(Guid patientId, Guid relationshipId, CancellationToken cancellationToken);
}

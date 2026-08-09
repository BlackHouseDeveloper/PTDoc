using System.ComponentModel.DataAnnotations;
using PTDoc.Core.Models;

namespace PTDoc.Application.NoteTemplates;

public sealed class NoteTemplateSchemaDefinition
{
    public int SchemaVersion { get; set; } = 1;
    public List<NoteTemplateSectionDefinition> Sections { get; set; } = [];
}

public sealed class NoteTemplateSectionDefinition
{
    [Required, MaxLength(100)] public string Key { get; set; } = string.Empty;
    [Required, MaxLength(150)] public string Label { get; set; } = string.Empty;
    [MaxLength(100)] public string? RendererKey { get; set; }
    public int Order { get; set; }
    public bool IsVisible { get; set; } = true;
    public List<NoteTemplateFieldDefinition> Fields { get; set; } = [];
}

public sealed class NoteTemplateFieldDefinition
{
    [Required, MaxLength(100)] public string Key { get; set; } = string.Empty;
    [Required, MaxLength(150)] public string Label { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string BindingKey { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string RendererKey { get; set; } = string.Empty;
    [MaxLength(500)] public string? HelpText { get; set; }
    [MaxLength(500)] public string? DefaultValue { get; set; }
    [MaxLength(100)] public string? ChoiceSourceKey { get; set; }
    public int Order { get; set; }
    public bool IsVisible { get; set; } = true;
    public bool IsRequired { get; set; }
    public List<NoteTemplateChoiceDefinition> Choices { get; set; } = [];
    public List<NoteTemplateConditionDefinition> VisibilityConditions { get; set; } = [];
}

public sealed class NoteTemplateChoiceDefinition
{
    [Required, MaxLength(100)] public string Value { get; set; } = string.Empty;
    [Required, MaxLength(150)] public string Label { get; set; } = string.Empty;
    public int Order { get; set; }
}

public sealed class NoteTemplateConditionDefinition
{
    [Required, MaxLength(100)] public string SourceBindingKey { get; set; } = string.Empty;
    public NoteTemplateConditionOperator Operator { get; set; }
    [MaxLength(500)] public string? ExpectedValue { get; set; }
}

public enum NoteTemplateConditionOperator { Equals = 0, NotEquals = 1, IsEmpty = 2, IsNotEmpty = 3, Contains = 4 }

public sealed class NoteTemplateSummaryDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public NoteType NoteType { get; set; }
    public NoteTemplateVariant Variant { get; set; }
    public int? ActiveVersionNumber { get; set; }
    public Guid? ActiveVersionId { get; set; }
    public Guid? LatestVersionId { get; set; }
    public NoteTemplateVersionStatus? LatestStatus { get; set; }
    public DateTime LastModifiedUtc { get; set; }
}

public sealed class NoteTemplateVersionDto
{
    public Guid Id { get; set; }
    public Guid NoteTemplateId { get; set; }
    public int VersionNumber { get; set; }
    public NoteTemplateVersionStatus Status { get; set; }
    public NoteTemplateSchemaDefinition Schema { get; set; } = new();
    public Guid CreatedByUserId { get; set; }
    public Guid? SubmittedByUserId { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public DateTime LastModifiedUtc { get; set; }
    public string? ReviewComment { get; set; }
}

public sealed class CreateNoteTemplateDraftRequest
{
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    public NoteType NoteType { get; set; }
    public NoteTemplateVariant Variant { get; set; }
    public Guid? CloneVersionId { get; set; }
}

public sealed class UpdateNoteTemplateDraftRequest
{
    [Required] public NoteTemplateSchemaDefinition Schema { get; set; } = new();
    public DateTime ExpectedLastModifiedUtc { get; set; }
}

public sealed class NoteTemplateReviewRequest
{
    [MaxLength(1000)] public string? Comment { get; set; }
}

public sealed class NoteTemplateValidationResult
{
    public bool IsValid => Errors.Count == 0;
    public List<string> Errors { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
}

public interface INoteTemplateAdministrationService
{
    Task<IReadOnlyList<NoteTemplateSummaryDto>> ListAsync(NoteType? noteType, NoteTemplateVersionStatus? status, CancellationToken cancellationToken);
    Task<IReadOnlyList<NoteTemplateSummaryDto>> ListForClinicalReviewAsync(NoteType? noteType, NoteTemplateVersionStatus? status, CancellationToken cancellationToken);
    Task<NoteTemplateVersionDto?> GetVersionAsync(Guid versionId, CancellationToken cancellationToken);
    Task<IReadOnlyList<NoteTemplateVersionDto>> ListVersionsAsync(Guid templateId, CancellationToken cancellationToken);
    Task<NoteTemplateVersionDto> CreateDraftAsync(CreateNoteTemplateDraftRequest request, CancellationToken cancellationToken);
    Task<NoteTemplateVersionDto> UpdateDraftAsync(Guid versionId, UpdateNoteTemplateDraftRequest request, CancellationToken cancellationToken);
    Task<NoteTemplateVersionDto> SubmitAsync(Guid versionId, CancellationToken cancellationToken);
    Task<NoteTemplateVersionDto> PublishAsync(Guid versionId, NoteTemplateReviewRequest request, CancellationToken cancellationToken);
    Task<NoteTemplateVersionDto> RejectAsync(Guid versionId, NoteTemplateReviewRequest request, CancellationToken cancellationToken);
    Task<NoteTemplateVersionDto> RetireAsync(Guid versionId, NoteTemplateReviewRequest request, CancellationToken cancellationToken);
    Task<NoteTemplateVersionDto> ResolveAsync(NoteType noteType, NoteTemplateVariant variant, CancellationToken cancellationToken);
    Task<NoteTemplateValidationResult> ValidateAsync(NoteType noteType, NoteTemplateVariant variant, NoteTemplateSchemaDefinition schema, CancellationToken cancellationToken);
    NoteTemplateValidationResult Validate(NoteType noteType, NoteTemplateVariant variant, NoteTemplateSchemaDefinition schema);
}

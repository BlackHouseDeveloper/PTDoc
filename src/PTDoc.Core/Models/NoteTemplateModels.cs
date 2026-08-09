namespace PTDoc.Core.Models;

public sealed class NoteTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ClinicId { get; set; }
    public NoteType NoteType { get; set; }
    public NoteTemplateVariant Variant { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid? ActiveVersionId { get; set; }
    public bool IsArchived { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastModifiedUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
    public Guid ModifiedByUserId { get; set; }

    public Clinic? Clinic { get; set; }
    public NoteTemplateVersion? ActiveVersion { get; set; }
    public ICollection<NoteTemplateVersion> Versions { get; set; } = new List<NoteTemplateVersion>();
}

public sealed class NoteTemplateVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid NoteTemplateId { get; set; }
    public Guid? ClinicId { get; set; }
    public int VersionNumber { get; set; }
    public NoteTemplateVersionStatus Status { get; set; } = NoteTemplateVersionStatus.Draft;
    public string SchemaJson { get; set; } = "{\"sections\":[]}";
    public Guid CreatedByUserId { get; set; }
    public Guid? SubmittedByUserId { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastModifiedUtc { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public DateTime? RetiredAtUtc { get; set; }
    public string? ReviewComment { get; set; }

    public NoteTemplate? Template { get; set; }
    public Clinic? Clinic { get; set; }
}

public enum NoteTemplateVariant { Standard = 0, ReEvaluation = 1, DryNeedling = 2 }
public enum NoteTemplateVersionStatus { Draft = 0, PendingClinicalApproval = 1, Published = 2, Rejected = 3, Retired = 4 }

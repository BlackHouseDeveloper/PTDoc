using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Compliance;
using PTDoc.Application.Identity;
using PTDoc.Application.NoteTemplates;
using PTDoc.Application.Notes.Workspace;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Infrastructure.Services;

public sealed class NoteTemplateAdministrationService(
    ApplicationDbContext db,
    ITenantContextAccessor tenantContext,
    IIdentityContextAccessor identityContext,
    IAuditService auditService) : INoteTemplateAdministrationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> Renderers = new(StringComparer.OrdinalIgnoreCase)
    {
        "specialized-section", "text", "textarea", "number", "date", "select", "multi-select", "checkbox", "outcome-measures", "goals", "billing"
    };
    private static readonly HashSet<string> ChoiceSources = new(StringComparer.OrdinalIgnoreCase)
    {
        "body-regions", "functional-limitations", "assistive-devices", "comorbidities", "special-tests", "outcome-measures", "treatment-interventions", "cpt", "icd10"
    };
    private static readonly HashSet<string> SpecializedSectionKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "subjective", "objective", "interventions", "assessment", "plan", "review",
        "progress-questionnaire", "discharge", "dry-needling"
    };
    private static readonly HashSet<string> RegisteredBindings = BuildRegisteredBindings();

    public async Task<IReadOnlyList<NoteTemplateSummaryDto>> ListAsync(NoteType? noteType, NoteTemplateVersionStatus? status, CancellationToken cancellationToken)
    {
        var query = db.NoteTemplates.AsNoTracking().Include(t => t.ActiveVersion).Include(t => t.Versions).Where(t => !t.IsArchived);
        if (noteType.HasValue) query = query.Where(t => t.NoteType == noteType.Value);
        if (status.HasValue) query = query.Where(t => t.Versions.Any(v => v.Status == status.Value));
        var rows = await query.OrderBy(t => t.NoteType).ThenBy(t => t.Variant).ThenBy(t => t.Name).ToListAsync(cancellationToken);
        return rows.Select(t => { var latest = t.Versions.OrderByDescending(v => v.VersionNumber).FirstOrDefault(); return new NoteTemplateSummaryDto { Id = t.Id, Name = t.Name, NoteType = t.NoteType, Variant = t.Variant, ActiveVersionId = t.ActiveVersionId, ActiveVersionNumber = t.ActiveVersion?.VersionNumber, LatestVersionId = latest?.Id, LatestStatus = latest?.Status, LastModifiedUtc = t.LastModifiedUtc }; }).ToList();
    }

    public async Task<IReadOnlyList<NoteTemplateSummaryDto>> ListForClinicalReviewAsync(NoteType? noteType, NoteTemplateVersionStatus? status, CancellationToken cancellationToken)
    {
        var rows = await ListAsync(noteType, status, cancellationToken);
        return rows.OrderBy(row => row.LatestStatus == NoteTemplateVersionStatus.PendingClinicalApproval ? 0 : 1)
            .ThenBy(row => row.NoteType)
            .ThenBy(row => row.Variant)
            .ToList();
    }

    public async Task<NoteTemplateVersionDto?> GetVersionAsync(Guid versionId, CancellationToken cancellationToken)
    {
        var row = await db.NoteTemplateVersions.AsNoTracking().FirstOrDefaultAsync(v => v.Id == versionId, cancellationToken);
        return row is null ? null : Map(row);
    }

    public async Task<IReadOnlyList<NoteTemplateVersionDto>> ListVersionsAsync(Guid templateId, CancellationToken cancellationToken)
    {
        if (!await db.NoteTemplates.AsNoTracking().AnyAsync(template => template.Id == templateId && !template.IsArchived, cancellationToken)) throw new KeyNotFoundException("Note template not found.");
        var rows = await db.NoteTemplateVersions.AsNoTracking().Where(version => version.NoteTemplateId == templateId).OrderByDescending(version => version.VersionNumber).ToListAsync(cancellationToken);
        return rows.Select(Map).ToList();
    }

    public async Task<NoteTemplateVersionDto> CreateDraftAsync(CreateNoteTemplateDraftRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) throw new ArgumentException("Template name is required.");
        ValidateVariant(request.NoteType, request.Variant);
        var clinicId = RequireClinic(); var userId = identityContext.GetCurrentUserId(); var now = DateTime.UtcNow;
        NoteTemplate template;
        NoteTemplateSchemaDefinition schema;
        if (request.CloneVersionId.HasValue)
        {
            var source = await db.NoteTemplateVersions.AsNoTracking().Include(v => v.Template).FirstOrDefaultAsync(v => v.Id == request.CloneVersionId.Value, cancellationToken) ?? throw new KeyNotFoundException("Template version not found.");
            if (source.Template!.NoteType != request.NoteType || source.Template.Variant != request.Variant) throw new InvalidOperationException("A template can only be cloned into the same note type and variant.");
            schema = Deserialize(source.SchemaJson);
        }
        else schema = BuildBaseline(request.NoteType, request.Variant);
        var validation = Validate(request.NoteType, request.Variant, schema); if (!validation.IsValid) throw new ArgumentException(string.Join(" ", validation.Errors));
        template = await db.NoteTemplates.Include(t => t.Versions).FirstOrDefaultAsync(t => t.NoteType == request.NoteType && t.Variant == request.Variant && !t.IsArchived, cancellationToken)
            ?? new NoteTemplate { ClinicId = clinicId, NoteType = request.NoteType, Variant = request.Variant, Name = request.Name.Trim(), CreatedAtUtc = now, CreatedByUserId = userId };
        if (template.Id == default) template.Id = Guid.NewGuid();
        if (db.Entry(template).State == EntityState.Detached) db.NoteTemplates.Add(template);
        template.Name = request.Name.Trim(); template.LastModifiedUtc = now; template.ModifiedByUserId = userId;
        var next = template.Versions.Count == 0 ? 1 : template.Versions.Max(v => v.VersionNumber) + 1;
        var version = new NoteTemplateVersion { NoteTemplateId = template.Id, ClinicId = clinicId, VersionNumber = next, Status = NoteTemplateVersionStatus.Draft, SchemaJson = Serialize(schema), CreatedByUserId = userId, CreatedAtUtc = now, LastModifiedUtc = now };
        db.NoteTemplateVersions.Add(version); await db.SaveChangesAsync(cancellationToken); await AuditAsync("NoteTemplateDraftCreated", version.Id, userId, cancellationToken); return Map(version);
    }

    public async Task<NoteTemplateVersionDto> UpdateDraftAsync(Guid versionId, UpdateNoteTemplateDraftRequest request, CancellationToken cancellationToken)
    {
        var row = await db.NoteTemplateVersions.Include(v => v.Template).FirstOrDefaultAsync(v => v.Id == versionId, cancellationToken) ?? throw new KeyNotFoundException("Template version not found.");
        if (row.Status != NoteTemplateVersionStatus.Draft && row.Status != NoteTemplateVersionStatus.Rejected) throw new InvalidOperationException("Only draft or rejected versions can be edited.");
        if (row.LastModifiedUtc != request.ExpectedLastModifiedUtc) throw new DbUpdateConcurrencyException("The template draft changed in another session.");
        var validation = Validate(row.Template!.NoteType, row.Template.Variant, request.Schema); if (!validation.IsValid) throw new ArgumentException(string.Join(" ", validation.Errors));
        row.SchemaJson = Serialize(request.Schema); row.Status = NoteTemplateVersionStatus.Draft; row.ReviewComment = null; row.LastModifiedUtc = DateTime.UtcNow; row.Template.LastModifiedUtc = row.LastModifiedUtc; row.Template.ModifiedByUserId = identityContext.GetCurrentUserId();
        await db.SaveChangesAsync(cancellationToken); return Map(row);
    }

    public async Task<NoteTemplateVersionDto> SubmitAsync(Guid versionId, CancellationToken cancellationToken)
    {
        var row = await LoadAsync(versionId, cancellationToken); if (row.Status != NoteTemplateVersionStatus.Draft && row.Status != NoteTemplateVersionStatus.Rejected) throw new InvalidOperationException("Only an editable draft can be submitted.");
        var validation = Validate(row.Template!.NoteType, row.Template.Variant, Deserialize(row.SchemaJson)); if (!validation.IsValid) throw new ArgumentException(string.Join(" ", validation.Errors));
        row.Status = NoteTemplateVersionStatus.PendingClinicalApproval; row.SubmittedByUserId = identityContext.GetCurrentUserId(); row.SubmittedAtUtc = DateTime.UtcNow; row.LastModifiedUtc = DateTime.UtcNow; await db.SaveChangesAsync(cancellationToken); await AuditAsync("NoteTemplateSubmitted", row.Id, row.SubmittedByUserId.Value, cancellationToken); return Map(row);
    }

    public async Task<NoteTemplateVersionDto> PublishAsync(Guid versionId, NoteTemplateReviewRequest request, CancellationToken cancellationToken)
    {
        var row = await LoadAsync(versionId, cancellationToken); if (row.Status != NoteTemplateVersionStatus.PendingClinicalApproval) throw new InvalidOperationException("Only a template awaiting clinical approval can be published.");
        var reviewer = identityContext.GetCurrentUserId(); if (row.CreatedByUserId == reviewer || row.SubmittedByUserId == reviewer) throw new InvalidOperationException("The clinical publisher must be different from the draft author and submitter.");
        var current = await db.NoteTemplateVersions.FirstOrDefaultAsync(v => v.NoteTemplateId == row.NoteTemplateId && v.Status == NoteTemplateVersionStatus.Published, cancellationToken); if (current is not null) { current.Status = NoteTemplateVersionStatus.Retired; current.RetiredAtUtc = DateTime.UtcNow; current.LastModifiedUtc = DateTime.UtcNow; }
        row.Status = NoteTemplateVersionStatus.Published; row.ReviewedByUserId = reviewer; row.ReviewComment = Trim(request.Comment); row.PublishedAtUtc = DateTime.UtcNow; row.LastModifiedUtc = DateTime.UtcNow; row.Template!.ActiveVersionId = row.Id; row.Template.LastModifiedUtc = row.LastModifiedUtc; row.Template.ModifiedByUserId = reviewer;
        await db.SaveChangesAsync(cancellationToken); await AuditAsync("NoteTemplatePublished", row.Id, reviewer, cancellationToken); return Map(row);
    }

    public async Task<NoteTemplateVersionDto> RejectAsync(Guid versionId, NoteTemplateReviewRequest request, CancellationToken cancellationToken)
    {
        var row = await LoadAsync(versionId, cancellationToken); if (row.Status != NoteTemplateVersionStatus.PendingClinicalApproval) throw new InvalidOperationException("Only a template awaiting clinical approval can be rejected.");
        var reviewer = identityContext.GetCurrentUserId(); row.Status = NoteTemplateVersionStatus.Rejected; row.ReviewedByUserId = reviewer; row.ReviewComment = Trim(request.Comment); row.LastModifiedUtc = DateTime.UtcNow; await db.SaveChangesAsync(cancellationToken); await AuditAsync("NoteTemplateRejected", row.Id, reviewer, cancellationToken); return Map(row);
    }

    public async Task<NoteTemplateVersionDto> RetireAsync(Guid versionId, NoteTemplateReviewRequest request, CancellationToken cancellationToken)
    {
        var row = await LoadAsync(versionId, cancellationToken); if (row.Status != NoteTemplateVersionStatus.Published) throw new InvalidOperationException("Only a published template can be retired.");
        var reviewer = identityContext.GetCurrentUserId(); row.Status = NoteTemplateVersionStatus.Retired; row.ReviewedByUserId = reviewer; row.ReviewComment = Trim(request.Comment); row.RetiredAtUtc = DateTime.UtcNow; row.LastModifiedUtc = DateTime.UtcNow; if (row.Template!.ActiveVersionId == row.Id) row.Template.ActiveVersionId = null; row.Template.LastModifiedUtc = row.LastModifiedUtc; row.Template.ModifiedByUserId = reviewer; await db.SaveChangesAsync(cancellationToken); await AuditAsync("NoteTemplateRetired", row.Id, reviewer, cancellationToken); return Map(row);
    }

    public async Task<NoteTemplateVersionDto> ResolveAsync(NoteType noteType, NoteTemplateVariant variant, CancellationToken cancellationToken)
    {
        ValidateVariant(noteType, variant);
        var version = await db.NoteTemplates.AsNoTracking().Where(t => t.NoteType == noteType && t.Variant == variant && !t.IsArchived && t.ActiveVersionId != null).Select(t => t.ActiveVersion).FirstOrDefaultAsync(cancellationToken);
        return version is null ? new NoteTemplateVersionDto { Id = Guid.Empty, NoteTemplateId = Guid.Empty, VersionNumber = 0, Status = NoteTemplateVersionStatus.Published, Schema = BuildBaseline(noteType, variant), LastModifiedUtc = DateTime.MinValue } : Map(version);
    }

    public NoteTemplateValidationResult Validate(NoteType noteType, NoteTemplateVariant variant, NoteTemplateSchemaDefinition schema)
    {
        var result = new NoteTemplateValidationResult();
        try { ValidateVariant(noteType, variant); } catch (ArgumentException ex) { result.Errors.Add(ex.Message); }
        if (schema.SchemaVersion != 1) result.Errors.Add("Only note template schema version 1 is supported.");
        if (schema.Sections.Count == 0) result.Errors.Add("At least one template section is required.");
        foreach (var duplicate in schema.Sections.GroupBy(s => s.Key, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1)) result.Errors.Add($"Duplicate section key '{duplicate.Key}'.");
        foreach (var duplicate in schema.Sections.GroupBy(s => s.Order).Where(g => g.Count() > 1)) result.Errors.Add($"Template sections must have unique order values; {duplicate.Key} is duplicated.");
        foreach (var section in schema.Sections)
        {
            if (string.IsNullOrWhiteSpace(section.Key) || string.IsNullOrWhiteSpace(section.Label)) result.Errors.Add("Every section requires a key and label.");
            if (!string.IsNullOrWhiteSpace(section.RendererKey) && !Renderers.Contains(section.RendererKey)) result.Errors.Add($"Unsupported section renderer '{section.RendererKey}'.");
            if (string.Equals(section.RendererKey, "specialized-section", StringComparison.OrdinalIgnoreCase) && !SpecializedSectionKeys.Contains(section.Key)) result.Errors.Add($"Unsupported specialized section '{section.Key}'.");
            foreach (var duplicate in section.Fields.GroupBy(f => f.Key, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1)) result.Errors.Add($"Duplicate field key '{duplicate.Key}' in section '{section.Key}'.");
            foreach (var duplicate in section.Fields.GroupBy(f => f.Order).Where(g => g.Count() > 1)) result.Errors.Add($"Fields in section '{section.Key}' must have unique order values; {duplicate.Key} is duplicated.");
            foreach (var field in section.Fields)
            {
                if (!Renderers.Contains(field.RendererKey)) result.Errors.Add($"Unsupported field renderer '{field.RendererKey}'.");
                if (!IsSupportedBinding(field.BindingKey)) result.Errors.Add($"Unsupported workspace binding '{field.BindingKey}'.");
                else if (!string.IsNullOrWhiteSpace(field.DefaultValue) && !IsSupportedDefaultValue(field.BindingKey, field.DefaultValue)) result.Errors.Add($"Default value for '{field.Label}' is not valid for workspace binding '{field.BindingKey}'.");
                if (!string.IsNullOrWhiteSpace(field.ChoiceSourceKey) && !ChoiceSources.Contains(field.ChoiceSourceKey)) result.Errors.Add($"Unsupported choice source '{field.ChoiceSourceKey}'.");
                if (field.Choices.Count > 0 && !string.IsNullOrWhiteSpace(field.ChoiceSourceKey)) result.Errors.Add($"Field '{field.Label}' cannot use both static choices and a catalog choice source.");
                foreach (var duplicate in field.Choices.GroupBy(choice => choice.Value, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1)) result.Errors.Add($"Duplicate choice value '{duplicate.Key}' in field '{field.Label}'.");
                if (field.Choices.Any(choice => string.IsNullOrWhiteSpace(choice.Value) || string.IsNullOrWhiteSpace(choice.Label))) result.Errors.Add($"Every static choice in field '{field.Label}' requires a value and label.");
                if ((string.Equals(field.RendererKey, "select", StringComparison.OrdinalIgnoreCase) || string.Equals(field.RendererKey, "multi-select", StringComparison.OrdinalIgnoreCase)) && field.Choices.Count == 0 && string.IsNullOrWhiteSpace(field.ChoiceSourceKey)) result.Errors.Add($"Choice field '{field.Label}' requires static choices or an approved choice source.");
                foreach (var condition in field.VisibilityConditions) { if (!IsSupportedBinding(condition.SourceBindingKey)) result.Errors.Add($"Unsupported condition binding '{condition.SourceBindingKey}'."); if (!Enum.IsDefined(condition.Operator)) result.Errors.Add($"Unsupported visibility operator '{condition.Operator}'."); }
            }
        }
        if (!schema.Sections.Any(s => s.Key.Equals("subjective", StringComparison.OrdinalIgnoreCase))) result.Warnings.Add("This template does not include a Subjective section.");
        if (!schema.Sections.Any(s => s.Key.Equals("review", StringComparison.OrdinalIgnoreCase) && s.IsVisible)) result.Errors.Add("The required Review and signature section cannot be disabled.");
        return result;
    }

    public Task<NoteTemplateValidationResult> ValidateAsync(NoteType noteType, NoteTemplateVariant variant, NoteTemplateSchemaDefinition schema, CancellationToken cancellationToken)
        => Task.FromResult(Validate(noteType, variant, schema));

    private async Task<NoteTemplateVersion> LoadAsync(Guid id, CancellationToken ct) => await db.NoteTemplateVersions.Include(v => v.Template).FirstOrDefaultAsync(v => v.Id == id, ct) ?? throw new KeyNotFoundException("Template version not found.");
    private Guid RequireClinic() => tenantContext.GetCurrentClinicId() ?? throw new InvalidOperationException("A clinic context is required.");
    private static bool IsSupportedBinding(string? key) => !string.IsNullOrWhiteSpace(key) && RegisteredBindings.Contains(key);
    private static bool IsSupportedDefaultValue(string bindingKey, string value)
    {
        var type = ResolveBindingType(bindingKey); if (type is null) return false;
        var target = Nullable.GetUnderlyingType(type) ?? type;
        try
        {
            if (target == typeof(string)) return true;
            if (target == typeof(Guid)) { _ = Guid.Parse(value); return true; }
            if (target == typeof(DateTime)) { _ = DateTime.Parse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind); return true; }
            if (target.IsEnum) { _ = Enum.Parse(target, value, true); return true; }
            if (typeof(System.Collections.IEnumerable).IsAssignableFrom(target)) { return JsonSerializer.Deserialize(value, target, JsonOptions) is not null; }
            _ = Convert.ChangeType(value, target, System.Globalization.CultureInfo.InvariantCulture); return true;
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException or ArgumentException or JsonException) { return false; }
    }
    private static Type? ResolveBindingType(string path)
    {
        var type = typeof(NoteWorkspaceV2Payload);
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var property = type.GetProperties().FirstOrDefault(candidate => string.Equals(JsonNamingPolicy.CamelCase.ConvertName(candidate.Name), segment, StringComparison.OrdinalIgnoreCase));
            if (property is null) return null; type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        }
        return type;
    }
    private static HashSet<string> BuildRegisteredBindings()
    {
        var bindings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddBindings(typeof(NoteWorkspaceV2Payload), string.Empty, bindings, 0);
        return bindings;
    }
    private static void AddBindings(Type type, string prefix, HashSet<string> bindings, int depth)
    {
        if (depth > 4) return;
        foreach (var property in type.GetProperties().Where(p => p.CanRead && p.CanWrite))
        {
            var name = JsonNamingPolicy.CamelCase.ConvertName(property.Name);
            var path = string.IsNullOrEmpty(prefix) ? name : $"{prefix}.{name}";
            bindings.Add(path);
            var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            if (propertyType == typeof(string) || propertyType.IsPrimitive || propertyType.IsEnum || propertyType == typeof(DateTime) || propertyType == typeof(Guid) || propertyType == typeof(decimal) || typeof(System.Collections.IEnumerable).IsAssignableFrom(propertyType)) continue;
            if (propertyType.Namespace?.StartsWith("PTDoc", StringComparison.Ordinal) == true) AddBindings(propertyType, path, bindings, depth + 1);
        }
    }
    private static void ValidateVariant(NoteType type, NoteTemplateVariant variant)
    {
        if (!Enum.IsDefined(type) || !Enum.IsDefined(variant))
            throw new ArgumentException("Note type and template variant must be supported values.");
        if (variant == NoteTemplateVariant.ReEvaluation && type != NoteType.Evaluation)
            throw new ArgumentException("Re-evaluation is only valid for Evaluation notes.");
        if (variant == NoteTemplateVariant.DryNeedling && type != NoteType.Daily)
            throw new ArgumentException("Dry Needling is only valid for Daily notes.");
    }
    private static NoteTemplateSchemaDefinition BuildBaseline(NoteType type, NoteTemplateVariant variant)
    {
        var sections = new List<NoteTemplateSectionDefinition> { new() { Key = "subjective", Label = "Subjective", RendererKey = "specialized-section", Order = 10 }, new() { Key = "objective", Label = "Objective", RendererKey = "specialized-section", Order = 20 }, new() { Key = "assessment", Label = "Assessment", RendererKey = "specialized-section", Order = 30 }, new() { Key = "plan", Label = "Plan", RendererKey = "specialized-section", Order = 40 }, new() { Key = "review", Label = "Review", RendererKey = "specialized-section", Order = 50 } };
        if (type is NoteType.Evaluation or NoteType.Daily or NoteType.ProgressNote) sections.Insert(2, new() { Key = "interventions", Label = "Interventions", RendererKey = "specialized-section", Order = 25 });
        if (type == NoteType.ProgressNote) sections.Insert(1, new() { Key = "progress-questionnaire", Label = "Progress Questionnaire", RendererKey = "specialized-section", Order = 15 });
        if (type == NoteType.Discharge) sections.Insert(4, new() { Key = "discharge", Label = "Discharge Summary", RendererKey = "specialized-section", Order = 35 });
        if (variant == NoteTemplateVariant.DryNeedling) sections.Insert(2, new() { Key = "dry-needling", Label = "Dry Needling Treatment", RendererKey = "specialized-section", Order = 18 });
        return new() { SchemaVersion = 1, Sections = sections };
    }
    private static string Serialize(NoteTemplateSchemaDefinition schema) => JsonSerializer.Serialize(schema, JsonOptions); private static NoteTemplateSchemaDefinition Deserialize(string json) => JsonSerializer.Deserialize<NoteTemplateSchemaDefinition>(json, JsonOptions) ?? new(); private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static NoteTemplateVersionDto Map(NoteTemplateVersion v) => new() { Id = v.Id, NoteTemplateId = v.NoteTemplateId, VersionNumber = v.VersionNumber, Status = v.Status, Schema = Deserialize(v.SchemaJson), CreatedByUserId = v.CreatedByUserId, SubmittedByUserId = v.SubmittedByUserId, ReviewedByUserId = v.ReviewedByUserId, LastModifiedUtc = v.LastModifiedUtc, ReviewComment = v.ReviewComment };
    private Task AuditAsync(string eventType, Guid id, Guid userId, CancellationToken ct) => auditService.LogRuleEvaluationAsync(new AuditEvent { EventType = eventType, EntityType = "NoteTemplateVersion", EntityId = id, UserId = userId, Success = true, Metadata = new() { ["LifecycleEvent"] = eventType } }, ct);
}

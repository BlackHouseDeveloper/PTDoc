using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Identity;
using PTDoc.Core.Models;
using PTDoc.Infrastructure.Communication;

namespace PTDoc.Infrastructure.Data;

/// <summary>
/// Application database context for PTDoc.
/// Supports both SQLite (local-first) and SQL Server (cloud) via provider configuration.
/// Sprint J: Tenant-aware query filtering scopes all clinical data to the current clinic.
/// </summary>
public class ApplicationDbContext : DbContext
{
    private readonly ITenantContextAccessor? _tenantContext;

    /// <summary>
    /// Primary constructor used at runtime — receives tenant context for per-clinic filtering.
    /// </summary>
    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        ITenantContextAccessor? tenantContext = null)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    // Tenant entity (Sprint J)
    public DbSet<Clinic> Clinics => Set<Clinic>();

    // Clinical entities
    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<ClinicalNote> ClinicalNotes => Set<ClinicalNote>();
    public DbSet<IntakeForm> IntakeForms => Set<IntakeForm>();
    public DbSet<PatientDocument> PatientDocuments => Set<PatientDocument>();
    public DbSet<PatientCommunicationLogEntry> PatientCommunicationLogEntries => Set<PatientCommunicationLogEntry>();
    public DbSet<AppointmentPaymentTransaction> AppointmentPaymentTransactions => Set<AppointmentPaymentTransaction>();

    // User & auth entities
    public DbSet<User> Users => Set<User>();
    public DbSet<ExternalIdentityMapping> ExternalIdentityMappings => Set<ExternalIdentityMapping>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<LoginAttempt> LoginAttempts => Set<LoginAttempt>();

    // System entities
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<CommunicationDeliveryLog> CommunicationDeliveryLogs => Set<CommunicationDeliveryLog>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<IntakeOtpChallenge> IntakeOtpChallenges => Set<IntakeOtpChallenge>();
    public DbSet<Signature> Signatures => Set<Signature>();
    public DbSet<RuleOverride> RuleOverrides => Set<RuleOverride>();
    public DbSet<ComplianceSettings> ComplianceSettings => Set<ComplianceSettings>();
    public DbSet<SyncQueueItem> SyncQueueItems => Set<SyncQueueItem>();
    public DbSet<SyncConflictArchive> SyncConflictArchives => Set<SyncConflictArchive>();
    public DbSet<ExternalSystemMapping> ExternalSystemMappings => Set<ExternalSystemMapping>();
    public DbSet<ObjectiveMetric> ObjectiveMetrics => Set<ObjectiveMetric>();
    public DbSet<PatientGoal> PatientGoals => Set<PatientGoal>();

    // Sprint M: Outcome Measures (TDD §9)
    public DbSet<OutcomeMeasureResult> OutcomeMeasureResults => Set<OutcomeMeasureResult>();

    // First-class taxonomy filter index (see NoteTaxonomySelection)
    public DbSet<NoteTaxonomySelection> NoteTaxonomySelections => Set<NoteTaxonomySelection>();

    // Auth: Persisted refresh tokens (hashed; production replacement for InMemoryRefreshTokenStore)
    public DbSet<StoredRefreshToken> StoredRefreshTokens => Set<StoredRefreshToken>();

    // Notifications
    public DbSet<UserNotification> UserNotifications => Set<UserNotification>();
    public DbSet<UserNotificationPreferences> UserNotificationPreferences => Set<UserNotificationPreferences>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        NormalizeTrackedUsers();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        NormalizeTrackedUsers();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure Patient
        modelBuilder.Entity<Patient>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.LastModifiedUtc);
            entity.HasIndex(e => new { e.FirstName, e.LastName });
            entity.HasIndex(e => e.MedicalRecordNumber).IsUnique().HasFilter(IsNotNullFilter("MedicalRecordNumber"));
            entity.HasIndex(e => e.Email).HasFilter(IsNotNullFilter("Email"));

            entity.Property(e => e.FirstName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.LastName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Email).HasMaxLength(255);
            entity.Property(e => e.Phone).HasMaxLength(20);
            entity.Property(e => e.MedicalRecordNumber).HasMaxLength(50);
            entity.Property(e => e.PhysicianNpi).HasMaxLength(10);
            entity.Property(e => e.EmergencyContactPhone).HasMaxLength(20);
            entity.Property(e => e.DiagnosisCodesJson).IsRequired().HasDefaultValue("[]");

            // Relationships
            entity.HasMany(e => e.Appointments)
                .WithOne(e => e.Patient)
                .HasForeignKey(e => e.PatientId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(e => e.ClinicalNotes)
                .WithOne(e => e.Patient)
                .HasForeignKey(e => e.PatientId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(e => e.IntakeForms)
                .WithOne(e => e.Patient)
                .HasForeignKey(e => e.PatientId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Configure Appointment
        modelBuilder.Entity<Appointment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.PatientId);
            entity.HasIndex(e => e.StartTimeUtc);
            entity.HasIndex(e => new { e.ClinicalId, e.StartTimeUtc });
            entity.HasIndex(e => e.LastModifiedUtc);

            entity.Property(e => e.Notes).HasMaxLength(1000);
            entity.Property(e => e.CancellationReason).HasMaxLength(500);
        });

        // Configure ClinicalNote
        modelBuilder.Entity<ClinicalNote>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.PatientId);
            entity.HasIndex(e => e.DateOfService);
            entity.HasIndex(e => e.CreatedUtc);
            entity.HasIndex(e => e.ParentNoteId);
            entity.HasIndex(e => e.SignedUtc);
            entity.HasIndex(e => e.LastModifiedUtc);

            entity.Property(e => e.SignatureHash).HasMaxLength(64); // SHA-256 hex string
            entity.Property(e => e.PhysicianSignatureHash).HasMaxLength(64);
            entity.Property(e => e.TherapistNpi).HasMaxLength(10);

            // Relationship to Appointment (optional)
            entity.HasOne(e => e.Appointment)
                .WithMany()
                .HasForeignKey(e => e.AppointmentId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(e => e.ParentNote)
                .WithMany(e => e.Addendums)
                .HasForeignKey(e => e.ParentNoteId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Configure IntakeForm
        modelBuilder.Entity<IntakeForm>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.PatientId);
            entity.HasIndex(e => e.AccessToken).IsUnique();
            entity.HasIndex(e => e.LastModifiedUtc);
            entity.HasIndex(e => e.ReviewedAtUtc);

            entity.Property(e => e.TemplateVersion).HasMaxLength(50).IsRequired();
            entity.Property(e => e.AccessToken).HasMaxLength(256).IsRequired();
            var inviteTokenProperty = entity.Property(e => e.InviteToken);
            if (Database.ProviderName?.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) == true)
            {
                inviteTokenProperty.HasColumnType("nvarchar(max)");
            }
            else
            {
                inviteTokenProperty.HasMaxLength(4096);
            }

            // Sprint O: TDD §5.2 IntakeResponse contract fields
            entity.Property(e => e.PainMapData).IsRequired();
            entity.Property(e => e.Consents).IsRequired();
        });

        // Configure User
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Username).IsUnique();
            entity.HasIndex(e => e.Email).IsUnique().HasFilter(IsNotNullFilter("Email"));
            entity.HasIndex(e => e.PhoneNumber).HasFilter(IsNotNullFilter("PhoneNumber"));
            entity.HasIndex(e => e.NormalizedPhoneNumber).HasFilter(IsNotNullFilter("NormalizedPhoneNumber"));
            entity.HasIndex(e => e.IsActive);

            entity.Property(e => e.Username).HasMaxLength(100).IsRequired();
            entity.Property(e => e.PinHash).HasMaxLength(256).IsRequired();
            entity.Property(e => e.FirstName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.LastName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Email).HasMaxLength(255);
            entity.Property(e => e.PhoneNumber).HasMaxLength(30);
            entity.Property(e => e.NormalizedPhoneNumber).HasMaxLength(20);
            entity.Property(e => e.Role).HasMaxLength(50).IsRequired();
            entity.Property(e => e.LicenseNumber).HasMaxLength(50);
            entity.Property(e => e.LicenseState).HasMaxLength(2);

            // Relationships
            entity.HasMany(e => e.Sessions)
                .WithOne(e => e.User)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ExternalIdentityMapping>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.Provider, e.ExternalSubject }).IsUnique();
            entity.HasIndex(e => new { e.PrincipalType, e.InternalEntityId });
            entity.HasIndex(e => e.TenantId).HasFilter(IsNotNullFilter("TenantId"));
            entity.HasIndex(e => e.IsActive);

            entity.Property(e => e.Provider).HasMaxLength(100).IsRequired();
            entity.Property(e => e.ExternalSubject).HasMaxLength(255).IsRequired();
            entity.Property(e => e.PrincipalType).HasMaxLength(50).IsRequired();
        });

        // Configure Session
        modelBuilder.Entity<Session>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.TokenHash).IsUnique();
            entity.HasIndex(e => e.ExpiresAt);
            entity.HasIndex(e => new { e.IsRevoked, e.ExpiresAt });

            entity.Property(e => e.TokenHash).HasMaxLength(64).IsRequired(); // SHA-256 hex string
        });

        // Configure LoginAttempt
        modelBuilder.Entity<LoginAttempt>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Username);
            entity.HasIndex(e => e.UserId).HasFilter(IsNotNullFilter("UserId"));
            entity.HasIndex(e => e.AttemptedAt);
            entity.HasIndex(e => new { e.Success, e.AttemptedAt });

            entity.Property(e => e.Username).HasMaxLength(100).IsRequired();
            entity.Property(e => e.IpAddress).HasMaxLength(45); // IPv6 max length
            entity.Property(e => e.UserAgent).HasMaxLength(500);
            entity.Property(e => e.FailureReason).HasMaxLength(200);
        });

        // Configure AuditLog
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TimestampUtc);
            entity.HasIndex(e => e.EventType);
            entity.HasIndex(e => e.EntityId).HasFilter(IsNotNullFilter("EntityId"));
            entity.HasIndex(e => e.UserId).HasFilter(IsNotNullFilter("UserId"));
            entity.HasIndex(e => e.CorrelationId);
            entity.HasIndex(e => new { e.EntityType, e.EntityId }).HasFilter(IsNotNullFilter("EntityType", "EntityId"));

            entity.Property(e => e.EventType).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Severity).HasMaxLength(20).IsRequired();
            entity.Property(e => e.EntityType).HasMaxLength(100);
            entity.Property(e => e.CorrelationId).HasMaxLength(100).IsRequired();
            entity.Property(e => e.MetadataJson).IsRequired();
            entity.Property(e => e.ErrorMessage).HasMaxLength(2000);
        });

        modelBuilder.Entity<CommunicationDeliveryLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ClinicId).HasFilter(IsNotNullFilter("ClinicId"));
            entity.HasIndex(e => e.PatientId).HasFilter(IsNotNullFilter("PatientId"));
            entity.HasIndex(e => e.UserId).HasFilter(IsNotNullFilter("UserId"));
            entity.HasIndex(e => e.RecipientHash);
            entity.HasIndex(e => new { e.Purpose, e.Channel, e.CreatedAtUtc });
            entity.HasIndex(e => new { e.Purpose, e.Channel, e.CreatedAtUnixSeconds });
            entity.HasIndex(e => new { e.RecipientHash, e.Purpose, e.CreatedAtUnixSeconds });
            entity.HasIndex(e => new { e.PatientId, e.Purpose, e.CreatedAtUtc }).HasFilter(IsNotNullFilter("PatientId"));
            entity.HasIndex(e => new { e.PatientId, e.Purpose, e.CreatedAtUnixSeconds }).HasFilter(IsNotNullFilter("PatientId"));
            entity.HasIndex(e => e.CorrelationId).HasFilter(IsNotNullFilter("CorrelationId"));

            entity.Property(e => e.Purpose).HasConversion<string>().HasMaxLength(50).IsRequired();
            entity.Property(e => e.Channel).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(50).IsRequired();
            entity.Property(e => e.RecipientHash).HasMaxLength(128).IsRequired();
            entity.Property(e => e.Provider).HasMaxLength(100).IsRequired();
            entity.Property(e => e.ProviderMessageId).HasMaxLength(200);
            entity.Property(e => e.ErrorCode).HasMaxLength(100);
            entity.Property(e => e.SafeErrorMessage).HasMaxLength(500);
            entity.Property(e => e.CorrelationId).HasMaxLength(100);
        });

        modelBuilder.Entity<PasswordResetToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TokenHash).IsUnique();
            entity.HasIndex(e => new { e.UserId, e.ExpiresAtUtc });
            entity.HasIndex(e => new { e.RecipientHash, e.CreatedAtUtc });
            entity.HasIndex(e => e.CorrelationId).HasFilter(IsNotNullFilter("CorrelationId"));

            entity.Property(e => e.TokenHash).HasMaxLength(128).IsRequired();
            entity.Property(e => e.Channel).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(e => e.RecipientHash).HasMaxLength(128).IsRequired();
            entity.Property(e => e.RevocationReason).HasMaxLength(100);
            entity.Property(e => e.CorrelationId).HasMaxLength(100);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<IntakeOtpChallenge>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.IntakeId, e.Channel, e.ContactHash }).IsUnique();
            entity.HasIndex(e => new { e.PatientId, e.Channel, e.UpdatedAtUtc });
            entity.HasIndex(e => e.ClinicId).HasFilter(IsNotNullFilter("ClinicId"));
            entity.HasIndex(e => e.ExpiresAtUtc);
            entity.HasIndex(e => e.CorrelationId).HasFilter(IsNotNullFilter("CorrelationId"));

            entity.Property(e => e.Channel).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(e => e.ContactHash).HasMaxLength(128).IsRequired();
            entity.Property(e => e.OtpHash).HasMaxLength(128).IsRequired();
            entity.Property(e => e.CorrelationId).HasMaxLength(100);
        });

        modelBuilder.Entity<Signature>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.NoteId);
            entity.HasIndex(e => e.SignedByUserId);
            entity.HasIndex(e => e.TimestampUtc);

            entity.Property(e => e.Role).HasMaxLength(50).IsRequired();
            entity.Property(e => e.SignatureHash).HasMaxLength(64).IsRequired();
            entity.Property(e => e.AttestationText).IsRequired();
            entity.Property(e => e.IPAddress).HasMaxLength(45);
            entity.Property(e => e.DeviceInfo).HasMaxLength(500);

            entity.HasOne(e => e.Note)
                .WithMany()
                .HasForeignKey(e => e.NoteId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.SignedByUser)
                .WithMany()
                .HasForeignKey(e => e.SignedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PatientDocument>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.PatientId);
            entity.HasIndex(e => e.ClinicId).HasFilter(IsNotNullFilter("ClinicId"));
            entity.HasIndex(e => new { e.PatientId, e.UploadedAtUtc });
            entity.HasIndex(e => new { e.PatientId, e.DocumentType, e.UploadedAtUtc });
            entity.HasIndex(e => e.ContentHashSha256);

            entity.Property(e => e.DocumentType).HasMaxLength(80).IsRequired();
            entity.Property(e => e.FileName).HasMaxLength(255).IsRequired();
            entity.Property(e => e.ContentType).HasMaxLength(120).IsRequired();
            entity.Property(e => e.ContentHashSha256).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Notes).HasMaxLength(1000);
            entity.Property(e => e.ContentBytes).IsRequired();

            entity.HasOne(e => e.Patient)
                .WithMany(e => e.Documents)
                .HasForeignKey(e => e.PatientId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Clinic)
                .WithMany()
                .HasForeignKey(e => e.ClinicId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PatientCommunicationLogEntry>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.PatientId);
            entity.HasIndex(e => e.ClinicId).HasFilter(IsNotNullFilter("ClinicId"));
            entity.HasIndex(e => new { e.PatientId, e.OccurredAtUtc });
            entity.HasIndex(e => new { e.PatientId, e.Channel, e.OccurredAtUtc });

            entity.Property(e => e.Channel).HasMaxLength(40).IsRequired();
            entity.Property(e => e.Direction).HasMaxLength(40).IsRequired();
            entity.Property(e => e.Summary).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Details).HasMaxLength(2000);
            entity.Property(e => e.ContactName).HasMaxLength(120);

            entity.HasOne(e => e.Patient)
                .WithMany(e => e.CommunicationLogEntries)
                .HasForeignKey(e => e.PatientId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Clinic)
                .WithMany()
                .HasForeignKey(e => e.ClinicId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<RuleOverride>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.NoteId).HasFilter(IsNotNullFilter("NoteId"));
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.TimestampUtc);

            entity.Property(e => e.RuleName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Justification).IsRequired();
            entity.Property(e => e.AttestationText).IsRequired();

            entity.HasOne(e => e.Note)
                .WithMany()
                .HasForeignKey(e => e.NoteId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ComplianceSettings>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.OverrideAttestationText)
                .IsRequired()
                .HasDefaultValue(PTDoc.Core.Models.ComplianceSettings.DefaultOverrideAttestationText);
            entity.Property(e => e.MinJustificationLength)
                .IsRequired()
                .HasDefaultValue(20);
            entity.Property(e => e.AllowOverrideTypes)
                .IsRequired()
                .HasDefaultValue("[]");
        });

        // Configure SyncQueueItem
        modelBuilder.Entity<SyncQueueItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => new { e.Status, e.EnqueuedAt });
            entity.HasIndex(e => new { e.EntityType, e.EntityId });
            entity.HasIndex(e => e.EnqueuedAt);

            entity.Property(e => e.EntityType).HasMaxLength(100).IsRequired();
            entity.Property(e => e.ErrorMessage).HasMaxLength(2000);
        });

        // Configure ExternalSystemMapping
        modelBuilder.Entity<ExternalSystemMapping>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ExternalSystemName, e.ExternalId }).IsUnique();
            entity.HasIndex(e => e.InternalPatientId);
            entity.HasIndex(e => e.IsActive);

            entity.Property(e => e.ExternalSystemName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.ExternalId).HasMaxLength(255).IsRequired();

            // Relationship to Patient
            entity.HasOne(e => e.Patient)
                .WithMany()
                .HasForeignKey(e => e.InternalPatientId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AppointmentPaymentTransaction>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.AppointmentId);
            entity.HasIndex(e => e.AppointmentId)
                .IsUnique()
                .HasDatabaseName("UX_AppointmentPaymentTransactions_AppointmentId_Active")
                .HasFilter(AppointmentPaymentActiveStatusFilter());
            entity.HasIndex(e => e.PatientId);
            entity.HasIndex(e => new { e.AppointmentId, e.Status });
            entity.HasIndex(e => e.TransactionId);

            entity.Property(e => e.Amount).HasPrecision(18, 2);
            entity.Property(e => e.Status).IsRequired();
            entity.Property(e => e.Processor).HasMaxLength(100).IsRequired();
            entity.Property(e => e.TransactionId).HasMaxLength(120);
            entity.Property(e => e.AuthorizationCode).HasMaxLength(80);
            entity.Property(e => e.GatewayErrorCode).HasMaxLength(80);
            entity.Property(e => e.GatewayErrorMessage).HasMaxLength(500);
            entity.Property(e => e.InvoiceNumber).HasMaxLength(80);

            entity.HasOne(e => e.Appointment)
                .WithMany()
                .HasForeignKey(e => e.AppointmentId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Patient)
                .WithMany()
                .HasForeignKey(e => e.PatientId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Configure SyncConflictArchive
        modelBuilder.Entity<SyncConflictArchive>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.EntityType, e.EntityId });
            entity.HasIndex(e => e.DetectedAt);
            entity.HasIndex(e => e.IsResolved);

            entity.Property(e => e.EntityType).HasMaxLength(100).IsRequired();
            entity.Property(e => e.ResolutionType).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Reason).HasMaxLength(500).IsRequired();
            entity.Property(e => e.ResolutionNotes).HasMaxLength(1000);
        });

        // Configure ObjectiveMetric (Sprint O: TDD §5.4)
        modelBuilder.Entity<ObjectiveMetric>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.NoteId);

            entity.Property(e => e.Value).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Side).HasMaxLength(20);
            entity.Property(e => e.Unit).HasMaxLength(50);

            // Relationship to ClinicalNote
            entity.HasOne(e => e.Note)
                .WithMany(n => n.ObjectiveMetrics)
                .HasForeignKey(e => e.NoteId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PatientGoal>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.PatientId);
            entity.HasIndex(e => new { e.PatientId, e.Status });
            entity.HasIndex(e => e.ClinicId).HasFilter(IsNotNullFilter("ClinicId"));

            entity.Property(e => e.Description).HasMaxLength(2000).IsRequired();
            entity.Property(e => e.Category).HasMaxLength(200);
            entity.Property(e => e.MatchedFunctionalLimitationId).HasMaxLength(100);
            entity.Property(e => e.CompletionReason).HasMaxLength(1000);

            entity.HasOne(e => e.Patient)
                .WithMany()
                .HasForeignKey(e => e.PatientId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.OriginatingNote)
                .WithMany()
                .HasForeignKey(e => e.OriginatingNoteId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.MetByNote)
                .WithMany()
                .HasForeignKey(e => e.MetByNoteId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.ArchivedByNote)
                .WithMany()
                .HasForeignKey(e => e.ArchivedByNoteId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Clinic)
                .WithMany()
                .HasForeignKey(e => e.ClinicId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Auth: StoredRefreshToken — token hash is the unique lookup key
        modelBuilder.Entity<StoredRefreshToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TokenHash).IsUnique();
            entity.HasIndex(e => e.Subject);
            entity.HasIndex(e => e.ExpiresAtUtc);
            entity.HasIndex(e => e.IsRevoked);

            entity.Property(e => e.TokenHash).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Subject).HasMaxLength(255).IsRequired();
        });

        // Configure OutcomeMeasureResult (Sprint M: TDD §9)
        modelBuilder.Entity<OutcomeMeasureResult>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.PatientId);
            entity.HasIndex(e => new { e.PatientId, e.MeasureType });
            entity.HasIndex(e => e.DateRecorded);
            entity.HasIndex(e => e.ClinicId).HasFilter(IsNotNullFilter("ClinicId"));

            // Relationship to Patient
            entity.HasOne(e => e.Patient)
                .WithMany()
                .HasForeignKey(e => e.PatientId)
                .OnDelete(DeleteBehavior.Restrict);

            // Optional relationship to ClinicalNote
            entity.HasOne(e => e.Note)
                .WithMany()
                .HasForeignKey(e => e.NoteId)
                .OnDelete(DeleteBehavior.SetNull);

            // Optional relationship to Clinic (tenant)
            entity.HasOne(e => e.Clinic)
                .WithMany()
                .HasForeignKey(e => e.ClinicId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Sprint J: Configure Clinic (tenant) entity
        modelBuilder.Entity<Clinic>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Slug).IsUnique();
            entity.HasIndex(e => e.IsActive);

            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Slug).HasMaxLength(100).IsRequired();

            entity.HasMany(e => e.Users)
                .WithOne(e => e.Clinic)
                .HasForeignKey(e => e.ClinicId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(e => e.Patients)
                .WithOne(e => e.Clinic)
                .HasForeignKey(e => e.ClinicId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Sprint J: Configure ClinicId FK on tenant-scoped entities
        modelBuilder.Entity<Patient>(entity =>
        {
            entity.HasIndex(e => e.ClinicId).HasFilter(IsNotNullFilter("ClinicId"));
        });

        // Appointment, ClinicalNote, and IntakeForm carry ClinicId as a true FK to Clinic.
        // Denormalized from Patient for efficient per-clinic query filtering.
        modelBuilder.Entity<Appointment>(entity =>
        {
            entity.HasIndex(e => e.ClinicId).HasFilter(IsNotNullFilter("ClinicId"));
            entity.HasOne(e => e.Clinic)
                .WithMany()
                .HasForeignKey(e => e.ClinicId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ClinicalNote>(entity =>
        {
            entity.HasIndex(e => e.ClinicId).HasFilter(IsNotNullFilter("ClinicId"));
            entity.HasOne(e => e.Clinic)
                .WithMany()
                .HasForeignKey(e => e.ClinicId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<IntakeForm>(entity =>
        {
            entity.HasIndex(e => e.ClinicId).HasFilter(IsNotNullFilter("ClinicId"));
            entity.HasOne(e => e.Clinic)
                .WithMany()
                .HasForeignKey(e => e.ClinicId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasIndex(e => e.ClinicId).HasFilter(IsNotNullFilter("ClinicId"));
        });

        // Sprint J: Global query filters — automatically scope all clinical reads to current clinic.
        // Filters are bypassed when no tenant scope is active (system jobs, unauthenticated requests).
        // Use context.Set<T>().IgnoreQueryFilters() to intentionally bypass for admin operations.
        // Note: HasQueryFilter references `this` so the clinic ID is resolved per-query at runtime.
        //
        // Sprint S: Strict tenant isolation — removed the ClinicId == null pass-through.
        // Records without a ClinicId are no longer visible to any tenant-scoped context.
        // System contexts (CurrentClinicId == null) still see all records for admin/background jobs.
        modelBuilder.Entity<Patient>()
            .HasQueryFilter(p => CurrentClinicId == null || p.ClinicId == CurrentClinicId);

        modelBuilder.Entity<Appointment>()
            .HasQueryFilter(a => CurrentClinicId == null || a.ClinicId == CurrentClinicId);

        modelBuilder.Entity<ClinicalNote>()
            .HasQueryFilter(n => CurrentClinicId == null || n.ClinicId == CurrentClinicId);

        modelBuilder.Entity<IntakeForm>()
            .HasQueryFilter(f => CurrentClinicId == null || f.ClinicId == CurrentClinicId);

        modelBuilder.Entity<PatientDocument>()
            .HasQueryFilter(d => CurrentClinicId == null || d.ClinicId == CurrentClinicId);

        modelBuilder.Entity<PatientCommunicationLogEntry>()
            .HasQueryFilter(c => CurrentClinicId == null || c.ClinicId == CurrentClinicId);

        modelBuilder.Entity<AppointmentPaymentTransaction>()
            .HasQueryFilter(p => CurrentClinicId == null
                || ((p.Appointment == null || p.Appointment.ClinicId == CurrentClinicId)
                    && (p.Patient == null || p.Patient.ClinicId == CurrentClinicId)));

        modelBuilder.Entity<ExternalSystemMapping>()
            .HasQueryFilter(m => CurrentClinicId == null || m.Patient == null || m.Patient.ClinicId == CurrentClinicId);

        // Sprint O: ObjectiveMetric is accessed only through its parent ClinicalNote,
        // which already has its own query filter. Filter ObjectiveMetric via the note's ClinicId
        // so that direct queries on db.ObjectiveMetrics are also tenant-scoped.
        // Sprint S: null ClinicId on parent note is no longer permitted through the tenant filter.
        modelBuilder.Entity<ObjectiveMetric>()
            .HasQueryFilter(m => CurrentClinicId == null || m.Note!.ClinicId == CurrentClinicId);

        // Sprint M: OutcomeMeasureResult carries its own ClinicId for efficient tenant filtering.
        modelBuilder.Entity<OutcomeMeasureResult>()
            .HasQueryFilter(r => CurrentClinicId == null || r.ClinicId == CurrentClinicId);

        modelBuilder.Entity<PatientGoal>()
            .HasQueryFilter(g => CurrentClinicId == null || g.ClinicId == CurrentClinicId);

        // Configure UserNotification
        modelBuilder.Entity<UserNotification>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserId, e.IsArchived });
            entity.HasIndex(e => e.Timestamp);

            entity.Property(e => e.Title).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Message).HasMaxLength(1000).IsRequired();
            entity.Property(e => e.Type).HasMaxLength(50).IsRequired();
            entity.Property(e => e.TargetUrl).HasMaxLength(500);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure UserNotificationPreferences (one row per user)
        modelBuilder.Entity<UserNotificationPreferences>(entity =>
        {
            entity.HasKey(e => e.UserId);

            entity.HasOne(e => e.User)
                .WithOne()
                .HasForeignKey<UserNotificationPreferences>(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure NoteTaxonomySelection — first-class filter index for taxonomy queries
        modelBuilder.Entity<NoteTaxonomySelection>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ClinicalNoteId);
            entity.HasIndex(e => e.CategoryId);
            entity.HasIndex(e => e.ItemId);
            entity.HasIndex(e => new { e.CategoryId, e.ItemId });

            entity.Property(e => e.CategoryId).HasMaxLength(100).IsRequired();
            entity.Property(e => e.CategoryTitle).HasMaxLength(200).IsRequired();
            entity.Property(e => e.ItemId).HasMaxLength(100).IsRequired();
            entity.Property(e => e.ItemLabel).HasMaxLength(200).IsRequired();

            entity.HasOne(e => e.Note)
                .WithMany(n => n.TaxonomySelections)
                .HasForeignKey(e => e.ClinicalNoteId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // NoteTaxonomySelection is accessed via ClinicalNote; filter via the parent note's ClinicId.
        modelBuilder.Entity<NoteTaxonomySelection>()
            .HasQueryFilter(s => CurrentClinicId == null || s.Note!.ClinicId == CurrentClinicId);

        // Signature is accessed via ClinicalNote; filter via the parent note's ClinicId to prevent
        // cross-tenant signature visibility.
        modelBuilder.Entity<Signature>()
            .HasQueryFilter(s => CurrentClinicId == null || s.Note!.ClinicId == CurrentClinicId);

        // RuleOverride is tied to a specific note/clinic; filter via the parent note's ClinicId to
        // prevent cross-tenant override visibility. When NoteId is null (legacy rows), fall back to
        // the attesting user's ClinicId so those rows remain queryable within their clinic.
        modelBuilder.Entity<RuleOverride>()
            .HasQueryFilter(r => CurrentClinicId == null
                || (r.NoteId != null ? r.Note!.ClinicId == CurrentClinicId : r.User!.ClinicId == CurrentClinicId));

    }

    /// <summary>
    /// Returns the current tenant's clinic ID for use in global query filters.
    /// Evaluated at query execution time, not at model creation time.
    /// </summary>
    private Guid? CurrentClinicId => _tenantContext?.GetCurrentClinicId();

    private void NormalizeTrackedUsers()
    {
        var contactNormalizer = new ContactNormalizer();
        foreach (var entry in ChangeTracker.Entries<User>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            entry.Entity.Username = NormalizeUsername(entry.Entity.Username);
            entry.Entity.Email = NormalizeEmail(entry.Entity.Email);
            var normalizedPhone = contactNormalizer.NormalizePhone(entry.Entity.PhoneNumber);
            entry.Entity.NormalizedPhoneNumber = normalizedPhone.Succeeded
                ? normalizedPhone.NormalizedValue
                : null;
        }
    }

    private static string NormalizeUsername(string username)
    {
        ArgumentNullException.ThrowIfNull(username);

        var trimmed = username.Trim();
        return trimmed.Length == 0
            ? trimmed
            : trimmed.ToLowerInvariant();
    }

    private static string? NormalizeEmail(string? email)
    {
        if (email is null)
        {
            return null;
        }

        var trimmed = email.Trim();
        return trimmed.Length == 0
            ? trimmed
            : trimmed.ToLowerInvariant();
    }

    /// <summary>
    /// Returns a partial-index filter predicate appropriate for the configured database provider.
    /// PostgreSQL requires double-quoted identifiers for mixed-case column names in partial-index
    /// predicates (e.g. <c>"EntityId" IS NOT NULL</c>); SQL Server and SQLite are case-insensitive.
    /// Using this helper ensures future migrations scaffold correctly without manual edits.
    /// </summary>
    private string IsNotNullFilter(string column)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(column, @"^[a-zA-Z_][a-zA-Z0-9_]*$"))
        {
            throw new ArgumentException("Invalid column name for partial index filter.", nameof(column));
        }

        return Database.ProviderName?.Contains("Npgsql") == true
            ? $"\"{column}\" IS NOT NULL"
            : $"{column} IS NOT NULL";
    }

    /// <inheritdoc cref="IsNotNullFilter(string)"/>
    private string IsNotNullFilter(string column1, string column2)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(column1, @"^[a-zA-Z_][a-zA-Z0-9_]*$"))
        {
            throw new ArgumentException("Invalid column name for partial index filter.", nameof(column1));
        }

        if (!System.Text.RegularExpressions.Regex.IsMatch(column2, @"^[a-zA-Z_][a-zA-Z0-9_]*$"))
        {
            throw new ArgumentException("Invalid column name for partial index filter.", nameof(column2));
        }

        return Database.ProviderName?.Contains("Npgsql") == true
            ? $"\"{column1}\" IS NOT NULL AND \"{column2}\" IS NOT NULL"
            : $"{column1} IS NOT NULL AND {column2} IS NOT NULL";
    }

    private string AppointmentPaymentActiveStatusFilter()
    {
        var statusColumn = Database.ProviderName?.Contains("Npgsql") == true ? "\"Status\"" : "Status";
        return $"{statusColumn} IN ({(int)AppointmentPaymentStatus.Pending}, {(int)AppointmentPaymentStatus.Succeeded})";
    }
}

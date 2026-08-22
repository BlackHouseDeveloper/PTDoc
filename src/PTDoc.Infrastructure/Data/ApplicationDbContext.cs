using Microsoft.EntityFrameworkCore;
using PTDoc.Application.Identity;
using PTDoc.Application.Settings;
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
    public DbSet<ProviderDirectoryEntry> ProviderDirectoryEntries => Set<ProviderDirectoryEntry>();
    public DbSet<PatientProviderRelationship> PatientProviderRelationships => Set<PatientProviderRelationship>();
    public DbSet<PatientInsurancePolicy> PatientInsurancePolicies => Set<PatientInsurancePolicy>();
    public DbSet<PatientInsuranceAuthorization> PatientInsuranceAuthorizations => Set<PatientInsuranceAuthorization>();
    public DbSet<NoteTemplate> NoteTemplates => Set<NoteTemplate>();
    public DbSet<NoteTemplateVersion> NoteTemplateVersions => Set<NoteTemplateVersion>();

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
    public DbSet<IntegrationConnection> IntegrationConnections => Set<IntegrationConnection>();
    public DbSet<IntegrationExternalMapping> IntegrationExternalMappings => Set<IntegrationExternalMapping>();
    public DbSet<IntegrationOutboxItem> IntegrationOutboxItems => Set<IntegrationOutboxItem>();
    public DbSet<IntegrationSyncCheckpoint> IntegrationSyncCheckpoints => Set<IntegrationSyncCheckpoint>();
    public DbSet<IntegrationConflict> IntegrationConflicts => Set<IntegrationConflict>();
    public DbSet<ProcessedIntegrationWebhook> ProcessedIntegrationWebhooks => Set<ProcessedIntegrationWebhook>();
    public DbSet<FaxTransmission> FaxTransmissions => Set<FaxTransmission>();
    public DbSet<FaxRecipient> FaxRecipients => Set<FaxRecipient>();
    public DbSet<FaxStatusEvent> FaxStatusEvents => Set<FaxStatusEvent>();
    public DbSet<InboundFax> InboundFaxes => Set<InboundFax>();
    public DbSet<HepProgram> HepPrograms => Set<HepProgram>();
    public DbSet<HepProgramRevision> HepProgramRevisions => Set<HepProgramRevision>();
    public DbSet<HepPrescriptionExercise> HepPrescriptionExercises => Set<HepPrescriptionExercise>();
    public DbSet<HepTrackingObservation> HepTrackingObservations => Set<HepTrackingObservation>();
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

    // Clinic Settings administration
    public DbSet<RoleCapabilityPermission> RoleCapabilityPermissions => Set<RoleCapabilityPermission>();
    public DbSet<ClinicSecurityPolicy> ClinicSecurityPolicies => Set<ClinicSecurityPolicy>();
    public DbSet<UserMfaCredential> UserMfaCredentials => Set<UserMfaCredential>();
    public DbSet<UserMfaRecoveryCode> UserMfaRecoveryCodes => Set<UserMfaRecoveryCode>();
    public DbSet<VisitType> VisitTypes => Set<VisitType>();
    public DbSet<SchedulingPreferences> SchedulingPreferences => Set<SchedulingPreferences>();
    public DbSet<ClinicBusinessHour> ClinicBusinessHours => Set<ClinicBusinessHour>();
    public DbSet<ScheduleBlockRule> ScheduleBlockRules => Set<ScheduleBlockRule>();
    public DbSet<AppointmentReminderDispatch> AppointmentReminderDispatches => Set<AppointmentReminderDispatch>();
    public DbSet<AutoCheckInPolicy> AutoCheckInPolicies => Set<AutoCheckInPolicy>();
    public DbSet<KioskStation> KioskStations => Set<KioskStation>();
    public DbSet<KioskEnrollmentCode> KioskEnrollmentCodes => Set<KioskEnrollmentCode>();
    public DbSet<KioskCheckInToken> KioskCheckInTokens => Set<KioskCheckInToken>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        SeedTrackedClinicSettings();
        NormalizeTrackedAppointments();
        NormalizeTrackedUsers();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        SeedTrackedClinicSettings();
        NormalizeTrackedAppointments();
        NormalizeTrackedUsers();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        ConfigureSettingsModels(modelBuilder);

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
            entity.HasIndex(e => new { e.ClinicId, e.PatientId, e.ClinicalVisitOrdinal });
            entity.HasIndex(e => new { e.PatientId, e.ClinicalVisitOrdinal })
                .IsUnique()
                .HasDatabaseName("UX_Appointments_PatientId_ClinicalVisitOrdinal")
                .HasFilter(ClinicalVisitOrdinalFilter());
            entity.HasIndex(e => e.LastModifiedUtc);

            entity.Property(e => e.Notes).HasMaxLength(1000);
            entity.Property(e => e.CancellationReason).HasMaxLength(500);
            entity.Property(e => e.LastModifiedUtc).IsConcurrencyToken();
            entity.Property(e => e.AuthorizedOverlap).HasDefaultValue(false);

            entity.HasOne(e => e.VisitType)
                .WithMany()
                .HasForeignKey(e => new { e.ClinicId, e.VisitTypeId })
                .HasPrincipalKey(e => new { e.ClinicId, e.Id })
                .OnDelete(DeleteBehavior.Restrict);

            entity.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "CK_Appointments_VisitTypeRequiresClinic",
                    VisitTypeClinicCheckConstraint());

                // SQL Server's optimized OUTPUT-based DML is incompatible with the
                // TR_Appointments_PreventOverlap AFTER trigger. Keep the trigger as
                // the database-level scheduling guard and use trigger-compatible DML
                // for this table only.
                if (Database.IsSqlServer())
                {
                    table.UseSqlOutputClause(false);
                }
            });
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
            entity.Property(e => e.LastModifiedUtc).IsConcurrencyToken();

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

        modelBuilder.Entity<ProviderDirectoryEntry>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ClinicId, e.Status, e.LastName, e.FirstName });
            entity.HasIndex(e => new { e.ClinicId, e.Npi })
                .IsUnique()
                .HasFilter(ActiveProviderNpiFilter());
            entity.Property(e => e.FirstName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.LastName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Credentials).HasMaxLength(50);
            entity.Property(e => e.Npi).HasMaxLength(10);
            entity.Property(e => e.Specialty).HasMaxLength(150);
            entity.Property(e => e.TaxonomyCode).HasMaxLength(20);
            entity.Property(e => e.OrganizationName).HasMaxLength(200);
            entity.Property(e => e.Phone).HasMaxLength(30);
            entity.Property(e => e.Fax).HasMaxLength(30);
            entity.Property(e => e.Email).HasMaxLength(255);
            entity.Property(e => e.AddressLine1).HasMaxLength(200);
            entity.Property(e => e.AddressLine2).HasMaxLength(200);
            entity.Property(e => e.City).HasMaxLength(100);
            entity.Property(e => e.State).HasMaxLength(100);
            entity.Property(e => e.ZipCode).HasMaxLength(20);
            entity.Property(e => e.ReviewReason).HasMaxLength(500);
            entity.Property(e => e.LastModifiedUtc).IsConcurrencyToken();
            entity.HasOne(e => e.Clinic).WithMany().HasForeignKey(e => e.ClinicId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PatientProviderRelationship>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.PatientId, e.Role, e.IsArchived });
            entity.HasIndex(e => new { e.ClinicId, e.ProviderDirectoryEntryId });
            entity.Property(e => e.LastModifiedUtc).IsConcurrencyToken();
            entity.HasOne(e => e.Patient).WithMany(e => e.ProviderRelationships).HasForeignKey(e => e.PatientId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Provider).WithMany(e => e.PatientRelationships).HasForeignKey(e => e.ProviderDirectoryEntryId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Clinic).WithMany().HasForeignKey(e => e.ClinicId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PatientInsurancePolicy>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.PatientId, e.CoveragePriority })
                .IsUnique()
                .HasDatabaseName("UX_PatientInsurancePolicies_PatientId_CoveragePriority_Active")
                .HasFilter(ActiveInsurancePolicyFilter());
            entity.HasIndex(e => new { e.ClinicId, e.PatientId });
            entity.Property(e => e.CarrierKey).HasMaxLength(100);
            entity.Property(e => e.CarrierDisplayName).HasMaxLength(200);
            entity.Property(e => e.MemberOrPolicyNumber).HasMaxLength(100);
            entity.Property(e => e.GroupNumber).HasMaxLength(100);
            entity.Property(e => e.DeductibleAmount).HasPrecision(18, 2);
            entity.Property(e => e.DeductibleMet).HasPrecision(18, 2);
            entity.Property(e => e.OutOfPocketMaximum).HasPrecision(18, 2);
            entity.Property(e => e.OutOfPocketMet).HasPrecision(18, 2);
            entity.Property(e => e.CopayAmount).HasPrecision(18, 2);
            entity.Property(e => e.CoinsurancePercent).HasPrecision(5, 2);
            entity.Property(e => e.AdjusterName).HasMaxLength(150);
            entity.Property(e => e.AdjusterPhone).HasMaxLength(30);
            entity.Property(e => e.AdjusterEmail).HasMaxLength(255);
            entity.Property(e => e.AdjusterFax).HasMaxLength(30);
            entity.Property(e => e.LastModifiedUtc).IsConcurrencyToken();
            entity.HasOne(e => e.Patient).WithMany(e => e.InsurancePolicies).HasForeignKey(e => e.PatientId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Clinic).WithMany().HasForeignKey(e => e.ClinicId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PatientInsuranceAuthorization>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.PatientInsurancePolicyId, e.IsArchived });
            entity.HasIndex(e => new { e.ClinicId, e.PatientId });
            entity.Property(e => e.ReferenceNumber).HasMaxLength(100);
            entity.Property(e => e.AuthorizedUnits).HasPrecision(18, 2);
            entity.Property(e => e.UsedUnits).HasPrecision(18, 2);
            entity.Property(e => e.Notes).HasMaxLength(2000);
            entity.Property(e => e.LastModifiedUtc).IsConcurrencyToken();
            entity.HasOne(e => e.Policy).WithMany(e => e.Authorizations).HasForeignKey(e => e.PatientInsurancePolicyId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Patient).WithMany(e => e.InsuranceAuthorizations).HasForeignKey(e => e.PatientId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Clinic).WithMany().HasForeignKey(e => e.ClinicId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<NoteTemplate>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ClinicId, e.NoteType, e.Variant })
                .IsUnique()
                .HasDatabaseName("UX_NoteTemplates_ClinicId_NoteType_Variant_Active")
                .HasFilter(ActiveNoteTemplateFilter());
            entity.Property(e => e.Name).HasMaxLength(150).IsRequired();
            entity.Property(e => e.LastModifiedUtc).IsConcurrencyToken();
            entity.HasOne(e => e.Clinic).WithMany().HasForeignKey(e => e.ClinicId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.ActiveVersion).WithMany().HasForeignKey(e => e.ActiveVersionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<NoteTemplateVersion>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.NoteTemplateId, e.VersionNumber }).IsUnique();
            entity.HasIndex(e => new { e.ClinicId, e.Status });
            entity.Property(e => e.SchemaJson).IsRequired();
            entity.Property(e => e.ReviewComment).HasMaxLength(1000);
            entity.Property(e => e.LastModifiedUtc).IsConcurrencyToken();
            entity.HasOne(e => e.Template).WithMany(e => e.Versions).HasForeignKey(e => e.NoteTemplateId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Clinic).WithMany().HasForeignKey(e => e.ClinicId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ClinicalNote>()
            .HasOne(e => e.TemplateVersion)
            .WithMany()
            .HasForeignKey(e => e.TemplateVersionId)
            .OnDelete(DeleteBehavior.Restrict);

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
            entity.Property(e => e.MustChangePin).HasDefaultValue(false);
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
            entity.Property(e => e.StorageKey).HasMaxLength(1024);

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

        ConfigureIntegrationModels(modelBuilder);

        modelBuilder.Entity<AppointmentPaymentTransaction>(entity =>
        {
            entity.HasKey(e => e.Id);
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
            entity.Property(e => e.TimeZoneId)
                .HasMaxLength(100)
                .IsRequired()
                .HasDefaultValue("America/Los_Angeles");
            entity.Property(e => e.Version).IsConcurrencyToken();

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

        modelBuilder.Entity<ProviderDirectoryEntry>()
            .HasQueryFilter(e => CurrentClinicId == null || e.ClinicId == CurrentClinicId);
        modelBuilder.Entity<PatientProviderRelationship>()
            .HasQueryFilter(e => CurrentClinicId == null || e.ClinicId == CurrentClinicId);
        modelBuilder.Entity<PatientInsurancePolicy>()
            .HasQueryFilter(e => CurrentClinicId == null || e.ClinicId == CurrentClinicId);
        modelBuilder.Entity<PatientInsuranceAuthorization>()
            .HasQueryFilter(e => CurrentClinicId == null || e.ClinicId == CurrentClinicId);
        modelBuilder.Entity<NoteTemplate>()
            .HasQueryFilter(e => CurrentClinicId == null || e.ClinicId == CurrentClinicId);
        modelBuilder.Entity<NoteTemplateVersion>()
            .HasQueryFilter(e => CurrentClinicId == null || e.ClinicId == CurrentClinicId);

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

        // Integration records are clinic-scoped at the persistence boundary as
        // well as at API authorization checks. Child rows inherit scope through
        // their parent aggregate so a missed endpoint predicate cannot disclose
        // another clinic's fax or HEP data.
        modelBuilder.Entity<IntegrationConnection>()
            .HasQueryFilter(e => CurrentClinicId == null || e.ClinicId == CurrentClinicId);
        modelBuilder.Entity<IntegrationExternalMapping>()
            .HasQueryFilter(e => CurrentClinicId == null || e.ClinicId == CurrentClinicId);
        modelBuilder.Entity<IntegrationOutboxItem>()
            .HasQueryFilter(e => CurrentClinicId == null || e.ClinicId == CurrentClinicId);
        modelBuilder.Entity<IntegrationSyncCheckpoint>()
            .HasQueryFilter(e => CurrentClinicId == null || e.ClinicId == CurrentClinicId);
        modelBuilder.Entity<IntegrationConflict>()
            .HasQueryFilter(e => CurrentClinicId == null || e.ClinicId == CurrentClinicId);
        modelBuilder.Entity<ProcessedIntegrationWebhook>()
            .HasQueryFilter(e => CurrentClinicId == null || e.ClinicId == CurrentClinicId);
        modelBuilder.Entity<FaxTransmission>()
            .HasQueryFilter(e => CurrentClinicId == null || e.ClinicId == CurrentClinicId);
        modelBuilder.Entity<FaxRecipient>()
            .HasQueryFilter(e => CurrentClinicId == null || e.FaxTransmission!.ClinicId == CurrentClinicId);
        modelBuilder.Entity<FaxStatusEvent>()
            .HasQueryFilter(e => CurrentClinicId == null || e.FaxTransmission!.ClinicId == CurrentClinicId);
        modelBuilder.Entity<InboundFax>()
            .HasQueryFilter(e => CurrentClinicId == null || e.ClinicId == CurrentClinicId);
        modelBuilder.Entity<HepProgram>()
            .HasQueryFilter(e => CurrentClinicId == null || e.ClinicId == CurrentClinicId);
        modelBuilder.Entity<HepProgramRevision>()
            .HasQueryFilter(e => CurrentClinicId == null || e.HepProgram!.ClinicId == CurrentClinicId);
        modelBuilder.Entity<HepPrescriptionExercise>()
            .HasQueryFilter(e => CurrentClinicId == null || e.HepProgramRevision!.HepProgram!.ClinicId == CurrentClinicId);
        modelBuilder.Entity<HepTrackingObservation>()
            .HasQueryFilter(e => CurrentClinicId == null || e.ClinicId == CurrentClinicId);

        modelBuilder.Entity<UserMfaCredential>()
            .HasQueryFilter(e => CurrentClinicId == null || e.User!.ClinicId == CurrentClinicId);
        modelBuilder.Entity<UserMfaRecoveryCode>()
            .HasQueryFilter(e =>
                CurrentClinicId == null || e.Credential!.User!.ClinicId == CurrentClinicId);
        modelBuilder.Entity<RoleCapabilityPermission>()
            .HasQueryFilter(e => CurrentClinicId == null || e.ClinicId == CurrentClinicId);
        modelBuilder.Entity<ClinicSecurityPolicy>()
            .HasQueryFilter(e => CurrentClinicId == null || e.ClinicId == CurrentClinicId);
        modelBuilder.Entity<VisitType>()
            .HasQueryFilter(e => CurrentClinicId == null || e.ClinicId == CurrentClinicId);
        modelBuilder.Entity<SchedulingPreferences>()
            .HasQueryFilter(e => CurrentClinicId == null || e.ClinicId == CurrentClinicId);
        modelBuilder.Entity<ClinicBusinessHour>()
            .HasQueryFilter(e => CurrentClinicId == null || e.ClinicId == CurrentClinicId);
        modelBuilder.Entity<ScheduleBlockRule>()
            .HasQueryFilter(e => CurrentClinicId == null || e.ClinicId == CurrentClinicId);
        modelBuilder.Entity<AppointmentReminderDispatch>()
            .HasQueryFilter(e => CurrentClinicId == null || e.ClinicId == CurrentClinicId);
        modelBuilder.Entity<AutoCheckInPolicy>()
            .HasQueryFilter(e => CurrentClinicId == null || e.ClinicId == CurrentClinicId);
        modelBuilder.Entity<KioskStation>()
            .HasQueryFilter(e => CurrentClinicId == null || e.ClinicId == CurrentClinicId);
        modelBuilder.Entity<KioskEnrollmentCode>()
            .HasQueryFilter(e => CurrentClinicId == null || e.ClinicId == CurrentClinicId);
        modelBuilder.Entity<KioskCheckInToken>()
            .HasQueryFilter(e => CurrentClinicId == null || e.ClinicId == CurrentClinicId);

    }

    private static void ConfigureSettingsModels(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RoleCapabilityPermission>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ClinicId, e.RoleKey, e.CapabilityKey }).IsUnique();
            entity.Property(e => e.RoleKey).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Version).IsConcurrencyToken();
            entity.HasOne(e => e.Clinic).WithMany().HasForeignKey(e => e.ClinicId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ClinicSecurityPolicy>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ClinicId).IsUnique();
            entity.Property(e => e.Version).IsConcurrencyToken();
            entity.HasOne(e => e.Clinic).WithMany().HasForeignKey(e => e.ClinicId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserMfaCredential>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId).IsUnique();
            entity.Property(e => e.EncryptedSecret).HasMaxLength(2048).IsRequired();
            entity.Property(e => e.LastAcceptedTimeStep).IsConcurrencyToken();
            entity.HasOne(e => e.User)
                .WithOne(e => e.MfaCredential)
                .HasForeignKey<UserMfaCredential>(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserMfaRecoveryCode>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserMfaCredentialId, e.CodeHash }).IsUnique();
            entity.Property(e => e.CodeHash).HasMaxLength(256).IsRequired();
            entity.HasOne(e => e.Credential)
                .WithMany()
                .HasForeignKey(e => e.UserMfaCredentialId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<VisitType>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasAlternateKey(e => new { e.ClinicId, e.Id });
            entity.HasIndex(e => new { e.ClinicId, e.Code }).IsUnique();
            entity.HasIndex(e => new { e.ClinicId, e.IsActive, e.DisplayOrder });
            entity.Property(e => e.Code).HasMaxLength(80).IsRequired();
            entity.Property(e => e.Name).HasMaxLength(160).IsRequired();
            entity.Property(e => e.Version).IsConcurrencyToken();
            entity.HasOne(e => e.Clinic).WithMany().HasForeignKey(e => e.ClinicId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SchedulingPreferences>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ClinicId).IsUnique();
            entity.Property(e => e.DefaultClinicianView).HasMaxLength(30).IsRequired();
            entity.Property(e => e.DefaultAdminView).HasMaxLength(30).IsRequired();
            entity.Property(e => e.IntakeSentColor).HasMaxLength(7);
            entity.Property(e => e.IntakeIncompleteColor).HasMaxLength(7);
            entity.Property(e => e.IntakeCompleteColor).HasMaxLength(7);
            entity.Property(e => e.Version).IsConcurrencyToken();
            entity.HasOne(e => e.Clinic).WithMany().HasForeignKey(e => e.ClinicId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ClinicBusinessHour>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ClinicId, e.DayOfWeek }).IsUnique();
            entity.Property(e => e.Version).IsConcurrencyToken();
            entity.HasOne(e => e.Clinic).WithMany().HasForeignKey(e => e.ClinicId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ScheduleBlockRule>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ClinicId, e.IsActive });
            entity.HasIndex(e => new { e.ClinicId, e.ClinicianId });
            entity.HasIndex(e => e.ClinicianId);
            entity.Property(e => e.Name).HasMaxLength(160).IsRequired();
            entity.Property(e => e.ReasonCode).HasMaxLength(80).IsRequired();
            entity.Property(e => e.Version).IsConcurrencyToken();
            entity.HasOne(e => e.Clinic).WithMany().HasForeignKey(e => e.ClinicId).OnDelete(DeleteBehavior.Cascade);
            // User.ClinicId is nullable for legacy rows, so provider migrations strengthen this
            // optional EF relationship to (ClinicId, ClinicianId) at the database boundary.
            entity.HasOne<User>().WithMany().HasForeignKey(e => e.ClinicianId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AppointmentReminderDispatch>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.IdempotencyKey).IsUnique();
            entity.HasIndex(e => new { e.Status, e.NextAttemptAtUtc });
            entity.Property(e => e.IdempotencyKey).HasMaxLength(160).IsRequired();
            entity.Property(e => e.LastStatusCode).HasMaxLength(80);
            entity.HasOne(e => e.Clinic).WithMany().HasForeignKey(e => e.ClinicId).OnDelete(DeleteBehavior.Restrict);
            // Appointment.ClinicId is nullable for legacy rows, so EF cannot model it as a principal
            // alternate key without making the compatibility column required. Provider migrations
            // enforce the stronger (ClinicId, AppointmentId) boundary until ClinicId becomes required.
            entity.HasOne(e => e.Appointment).WithMany().HasForeignKey(e => e.AppointmentId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AutoCheckInPolicy>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ClinicId).IsUnique();
            entity.Property(e => e.TemplateKey).HasMaxLength(100).IsRequired();
            entity.Property(e => e.EligibleVisitTypeIdsJson).IsRequired();
            entity.Property(e => e.Version).IsConcurrencyToken();
            entity.HasOne(e => e.Clinic).WithMany().HasForeignKey(e => e.ClinicId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<KioskStation>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasAlternateKey(e => new { e.ClinicId, e.Id });
            entity.HasIndex(e => new { e.ClinicId, e.Name }).IsUnique();
            entity.Property(e => e.Name).HasMaxLength(120).IsRequired();
            entity.Property(e => e.DeviceCredentialHash).HasMaxLength(256).IsRequired();
            entity.Property(e => e.Version).IsConcurrencyToken();
            entity.HasOne(e => e.Clinic).WithMany().HasForeignKey(e => e.ClinicId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<KioskEnrollmentCode>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.CodeHash).IsUnique();
            entity.Property(e => e.CodeHash).HasMaxLength(256).IsRequired();
            entity.HasOne(e => e.Clinic).WithMany().HasForeignKey(e => e.ClinicId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.KioskStation)
                .WithMany()
                .HasForeignKey(e => new { e.ClinicId, e.KioskStationId })
                .HasPrincipalKey(e => new { e.ClinicId, e.Id })
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<KioskCheckInToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TokenHash).IsUnique();
            entity.Property(e => e.TokenHash).HasMaxLength(256).IsRequired();
            entity.HasOne(e => e.Clinic).WithMany().HasForeignKey(e => e.ClinicId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Appointment).WithMany().HasForeignKey(e => e.AppointmentId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    /// <summary>
    /// Returns the current tenant's clinic ID for use in global query filters.
    /// Evaluated at query execution time, not at model creation time.
    /// </summary>
    private Guid? CurrentClinicId => _tenantContext?.GetCurrentClinicId();

    private void SeedTrackedClinicSettings()
    {
        var newClinics = ChangeTracker.Entries<Clinic>()
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity)
            .ToArray();
        if (newClinics.Length == 0)
        {
            return;
        }

        var actorUserId = IIdentityContextAccessor.SystemUserId;
        foreach (var clinic in newClinics)
        {
            if (!ClinicSecurityPolicies.Local.Any(policy => policy.ClinicId == clinic.Id))
            {
                ClinicSecurityPolicies.Add(new ClinicSecurityPolicy
                {
                    ClinicId = clinic.Id,
                    UpdatedByUserId = actorUserId
                });
            }

            if (!SchedulingPreferences.Local.Any(preferences => preferences.ClinicId == clinic.Id))
            {
                SchedulingPreferences.Add(new SchedulingPreferences
                {
                    ClinicId = clinic.Id,
                    UpdatedByUserId = actorUserId
                });
            }

            if (!AutoCheckInPolicies.Local.Any(policy => policy.ClinicId == clinic.Id))
            {
                AutoCheckInPolicies.Add(new AutoCheckInPolicy
                {
                    ClinicId = clinic.Id,
                    UpdatedByUserId = actorUserId
                });
            }

            var trackedVisitTypeCodes = VisitTypes.Local
                .Where(visitType => visitType.ClinicId == clinic.Id)
                .Select(visitType => visitType.Code)
                .ToHashSet(StringComparer.Ordinal);
            VisitTypes.AddRange(SchedulingDefaults.VisitTypes
                .Where(definition => trackedVisitTypeCodes.Add(definition.Code))
                .Select(definition => new VisitType
                {
                    ClinicId = clinic.Id,
                    Code = definition.Code,
                    Name = definition.Name,
                    DurationMinutes = definition.DurationMinutes,
                    RequiresIntake = definition.RequiresIntake,
                    PtaAllowed = definition.PtaAllowed,
                    IsBillable = definition.IsBillable,
                    DisplayOrder = definition.DisplayOrder,
                    UpdatedByUserId = actorUserId
                }));

            var trackedBusinessDays = ClinicBusinessHours.Local
                .Where(hours => hours.ClinicId == clinic.Id)
                .Select(hours => hours.DayOfWeek)
                .ToHashSet();
            ClinicBusinessHours.AddRange(SchedulingDefaults.WeeklyHours
                .Where(definition => trackedBusinessDays.Add(definition.Day))
                .Select(definition => new ClinicBusinessHour
                {
                    ClinicId = clinic.Id,
                    DayOfWeek = definition.Day,
                    IsOpen = definition.IsOpen,
                    StartLocalTime = definition.IsOpen ? new TimeOnly(8, 0) : null,
                    EndLocalTime = definition.IsOpen ? new TimeOnly(17, 0) : null,
                    LunchStartLocalTime = definition.IsOpen ? new TimeOnly(12, 0) : null,
                    LunchEndLocalTime = definition.IsOpen ? new TimeOnly(13, 0) : null,
                    UpdatedByUserId = actorUserId
                }));

            var trackedPermissions = RoleCapabilityPermissions.Local
                .Where(permission => permission.ClinicId == clinic.Id)
                .Select(permission => (permission.RoleKey, permission.CapabilityKey))
                .ToHashSet();
            RoleCapabilityPermissions.AddRange(
                from role in RolePermissionCatalog.Roles
                from capability in RolePermissionCatalog.Capabilities
                where trackedPermissions.Add((role.Key, capability.Key))
                select new RoleCapabilityPermission
                {
                    ClinicId = clinic.Id,
                    RoleKey = role.Key,
                    CapabilityKey = capability.Key,
                    Level = capability.IsSupported
                        ? RolePermissionCatalog.GetCanonicalLevel(role.Key, capability.Key)
                        : PermissionLevel.None,
                    LockedMinimum = RolePermissionCatalog.GetLockedMinimum(role.Key, capability.Key),
                    UpdatedByUserId = actorUserId
                });
        }
    }

    private void NormalizeTrackedAppointments()
    {
        foreach (var entry in ChangeTracker.Entries<Appointment>()
                     .Where(candidate => candidate.State == EntityState.Modified))
        {
            var visitTypeId = entry.Property(appointment => appointment.VisitTypeId);
            var legacyTypeChanged = entry.Property(appointment => appointment.AppointmentType).IsModified;
            var clinicChanged = entry.Property(appointment => appointment.ClinicId).IsModified;
            if ((legacyTypeChanged || clinicChanged) && !visitTypeId.IsModified)
            {
                entry.Entity.VisitTypeId = null;
            }

            var schedulingFieldsChanged =
                entry.Property(appointment => appointment.ClinicalId).IsModified ||
                entry.Property(appointment => appointment.StartTimeUtc).IsModified ||
                entry.Property(appointment => appointment.EndTimeUtc).IsModified;
            var overlapAuthorization = entry.Property(appointment => appointment.AuthorizedOverlap);
            if (schedulingFieldsChanged && entry.Entity.AuthorizedOverlap && !overlapAuthorization.IsModified)
            {
                entry.Entity.AuthorizedOverlap = false;
            }
        }
    }

    public static void ConfigureIntegrationModels(ModelBuilder modelBuilder, bool includeBaseNavigations = true)
    {
        modelBuilder.Entity<IntegrationConnection>(entity =>
        {
            entity.ToTable("IntegrationConnections");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ClinicId, e.Provider }).IsUnique();
            entity.HasIndex(e => e.IsEnabled);
            entity.Property(e => e.Provider).HasMaxLength(50).IsRequired();
            entity.Property(e => e.DisplayName).HasMaxLength(160).IsRequired();
            entity.Property(e => e.ConfigurationJson).IsRequired();
            entity.Property(e => e.SecretReference).HasMaxLength(500).IsRequired();
            entity.Property(e => e.WebhookTokenHash).HasMaxLength(64);
            entity.Property(e => e.ComplianceApprovedAtUtc);
            entity.Property(e => e.ComplianceApprovedByUserId);
            entity.Property(e => e.CreatedAtUtc);
            entity.Property(e => e.UpdatedAtUtc);
            entity.Property(e => e.LastVerifiedAtUtc);
            entity.Property(e => e.LastHealthCode).HasMaxLength(100);
            if (includeBaseNavigations)
            {
                entity.HasOne(e => e.Clinic).WithMany().HasForeignKey(e => e.ClinicId).OnDelete(DeleteBehavior.Restrict);
            }
            else
            {
                entity.HasOne(typeof(Clinic).FullName!, navigationName: null).WithMany().HasForeignKey(nameof(IntegrationConnection.ClinicId)).OnDelete(DeleteBehavior.Restrict);
            }
        });

        modelBuilder.Entity<IntegrationExternalMapping>(entity =>
        {
            entity.ToTable("IntegrationExternalMappings");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.IntegrationConnectionId, e.EntityType, e.InternalEntityId })
                .HasDatabaseName("IX_IntExtMap_Conn_Entity_Internal")
                .IsUnique();
            entity.HasIndex(e => new { e.IntegrationConnectionId, e.EntityType, e.ExternalId })
                .HasDatabaseName("IX_IntExtMap_Conn_Entity_External")
                .IsUnique();
            entity.HasIndex(e => e.ClinicId);
            entity.Property(e => e.EntityType).HasMaxLength(80).IsRequired();
            entity.Property(e => e.ExternalId).HasMaxLength(255).IsRequired();
            entity.Property(e => e.IsActive);
            entity.Property(e => e.CreatedAtUtc);
            entity.Property(e => e.LastSyncedAtUtc);
            entity.HasOne(e => e.IntegrationConnection).WithMany().HasForeignKey(e => e.IntegrationConnectionId).OnDelete(DeleteBehavior.Cascade);
            if (includeBaseNavigations)
            {
                entity.HasOne(e => e.Clinic).WithMany().HasForeignKey(e => e.ClinicId).OnDelete(DeleteBehavior.Restrict);
            }
            else
            {
                entity.HasOne(typeof(Clinic).FullName!, navigationName: null).WithMany().HasForeignKey(nameof(IntegrationExternalMapping.ClinicId)).OnDelete(DeleteBehavior.Restrict);
            }
        });

        modelBuilder.Entity<IntegrationOutboxItem>(entity =>
        {
            entity.ToTable("IntegrationOutboxItems");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.Status, e.NextAttemptAtUtc });
            entity.HasIndex(e => new { e.IntegrationConnectionId, e.IdempotencyKey }).IsUnique();
            entity.HasIndex(e => new { e.AggregateType, e.AggregateId });
            entity.Property(e => e.JobType).HasMaxLength(100).IsRequired();
            entity.Property(e => e.AggregateType).HasMaxLength(80).IsRequired();
            entity.Property(e => e.IdempotencyKey).HasMaxLength(255).IsRequired();
            entity.Property(e => e.CorrelationId).HasMaxLength(100).IsRequired();
            entity.Property(e => e.LeaseOwner).HasMaxLength(160);
            entity.Property(e => e.LastErrorCode).HasMaxLength(160);
            entity.Property(e => e.PayloadJson).IsRequired();
            entity.Property(e => e.ClinicId);
            entity.Property(e => e.AttemptCount);
            entity.Property(e => e.MaxAttempts);
            entity.Property(e => e.LeaseExpiresAtUtc);
            entity.Property(e => e.CreatedAtUtc);
            entity.Property(e => e.UpdatedAtUtc);
            entity.Property(e => e.CompletedAtUtc);
            entity.HasOne(e => e.IntegrationConnection).WithMany().HasForeignKey(e => e.IntegrationConnectionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<IntegrationSyncCheckpoint>(entity =>
        {
            entity.ToTable("IntegrationSyncCheckpoints");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.IntegrationConnectionId, e.SyncType }).IsUnique();
            entity.Property(e => e.SyncType).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Cursor).HasMaxLength(500);
            entity.Property(e => e.ClinicId);
            entity.Property(e => e.LastSuccessfulAtUtc);
            entity.Property(e => e.UpdatedAtUtc);
            entity.HasOne(e => e.IntegrationConnection).WithMany().HasForeignKey(e => e.IntegrationConnectionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<IntegrationConflict>(entity =>
        {
            entity.ToTable("IntegrationConflicts");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.IntegrationConnectionId, e.Status });
            entity.HasIndex(e => new { e.EntityType, e.InternalEntityId });
            entity.Property(e => e.EntityType).HasMaxLength(80).IsRequired();
            entity.Property(e => e.ConflictType).HasMaxLength(100).IsRequired();
            entity.Property(e => e.DetailsJson).IsRequired();
            entity.Property(e => e.ClinicId);
            entity.Property(e => e.CreatedAtUtc);
            entity.Property(e => e.ResolvedAtUtc);
            entity.Property(e => e.ResolvedByUserId);
            entity.HasOne(e => e.IntegrationConnection).WithMany().HasForeignKey(e => e.IntegrationConnectionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProcessedIntegrationWebhook>(entity =>
        {
            entity.ToTable("ProcessedIntegrationWebhooks");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.IntegrationConnectionId, e.ProviderMessageId }).IsUnique();
            entity.Property(e => e.ProviderMessageId).HasMaxLength(255).IsRequired();
            entity.Property(e => e.EventType).HasMaxLength(100).IsRequired();
            entity.Property(e => e.PayloadHashSha256).HasMaxLength(64).IsRequired();
            entity.Property(e => e.ClinicId);
            entity.Property(e => e.ReceivedAtUtc);
            entity.HasOne(e => e.IntegrationConnection).WithMany().HasForeignKey(e => e.IntegrationConnectionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FaxTransmission>(entity =>
        {
            entity.ToTable("FaxTransmissions");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ClinicId, e.CreatedAtUtc });
            entity.HasIndex(e => e.PatientId);
            entity.HasIndex(e => e.ProviderFaxId);
            entity.HasIndex(e => new { e.IntegrationConnectionId, e.ClientCorrelationId }).IsUnique();
            entity.Property(e => e.ClientCorrelationId).HasMaxLength(100).IsRequired();
            entity.Property(e => e.ProviderFaxId).HasMaxLength(100);
            entity.Property(e => e.DocumentStorageKey).HasMaxLength(1024).IsRequired();
            entity.Property(e => e.DocumentFileName).HasMaxLength(255).IsRequired();
            entity.Property(e => e.DocumentContentType).HasMaxLength(120).IsRequired();
            entity.Property(e => e.DocumentHashSha256).HasMaxLength(64).IsRequired();
            entity.Property(e => e.DocumentType).HasMaxLength(80).IsRequired();
            entity.Property(e => e.CoverSubject).HasMaxLength(1045);
            entity.Property(e => e.CoverMessage).HasMaxLength(9945);
            entity.Property(e => e.ProviderStatus).HasMaxLength(100);
            entity.Property(e => e.FailureCode).HasMaxLength(160);
            entity.Property(e => e.OriginalTransmissionId);
            entity.Property(e => e.SourceDocumentId);
            entity.Property(e => e.SourceClinicalNoteId);
            entity.Property(e => e.RequestedByUserId);
            entity.Property(e => e.DocumentSizeBytes);
            entity.Property(e => e.IncludeCoverSheet);
            entity.Property(e => e.Status);
            entity.Property(e => e.UpdatedAtUtc);
            entity.Property(e => e.SubmittedAtUtc);
            entity.Property(e => e.CompletedAtUtc);
            if (includeBaseNavigations)
            {
                entity.HasOne(e => e.Clinic).WithMany().HasForeignKey(e => e.ClinicId).OnDelete(DeleteBehavior.Restrict);
            }
            else
            {
                entity.HasOne(typeof(Clinic).FullName!, navigationName: null).WithMany().HasForeignKey(nameof(FaxTransmission.ClinicId)).OnDelete(DeleteBehavior.Restrict);
            }
            entity.HasOne(e => e.IntegrationConnection).WithMany().HasForeignKey(e => e.IntegrationConnectionId).OnDelete(DeleteBehavior.Restrict);
            if (includeBaseNavigations)
            {
                entity.HasOne(e => e.Patient).WithMany().HasForeignKey(e => e.PatientId).OnDelete(DeleteBehavior.Restrict);
            }
            else
            {
                entity.HasOne(typeof(Patient).FullName!, navigationName: null).WithMany().HasForeignKey(nameof(FaxTransmission.PatientId)).OnDelete(DeleteBehavior.Restrict);
            }
        });

        modelBuilder.Entity<FaxRecipient>(entity =>
        {
            entity.ToTable("FaxRecipients");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.FaxTransmissionId);
            entity.Property(e => e.FaxNumber).HasMaxLength(20).IsRequired();
            entity.Property(e => e.RecipientName).HasMaxLength(245);
            entity.Property(e => e.ProviderStatus).HasMaxLength(100);
            entity.Property(e => e.FailureCode).HasMaxLength(160);
            entity.Property(e => e.Status);
            entity.Property(e => e.AttemptCount);
            entity.Property(e => e.CompletedAtUtc);
            entity.HasOne(e => e.FaxTransmission).WithMany(e => e.Recipients).HasForeignKey(e => e.FaxTransmissionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FaxStatusEvent>(entity =>
        {
            entity.ToTable("FaxStatusEvents");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.FaxTransmissionId, e.OccurredAtUtc });
            entity.Property(e => e.ProviderStatus).HasMaxLength(100);
            entity.Property(e => e.FailureCode).HasMaxLength(160);
            entity.Property(e => e.Source).HasMaxLength(40).IsRequired();
            entity.Property(e => e.Status);
            entity.HasOne(e => e.FaxTransmission).WithMany(e => e.StatusEvents).HasForeignKey(e => e.FaxTransmissionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<InboundFax>(entity =>
        {
            entity.ToTable("InboundFaxes");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.IntegrationConnectionId, e.ProviderFaxId }).IsUnique();
            entity.HasIndex(e => new { e.ClinicId, e.Status, e.ReceivedAtUtc });
            entity.HasIndex(e => e.AssignedPatientId);
            entity.HasIndex(e => e.PatientDocumentId);
            entity.Property(e => e.ProviderFaxId).HasMaxLength(100).IsRequired();
            entity.Property(e => e.ProviderStatus).HasMaxLength(100).IsRequired();
            entity.Property(e => e.FromNumber).HasMaxLength(20).IsRequired();
            entity.Property(e => e.ToNumber).HasMaxLength(20).IsRequired();
            entity.Property(e => e.SenderName).HasMaxLength(245);
            entity.Property(e => e.DocumentStorageKey).HasMaxLength(1024).IsRequired();
            entity.Property(e => e.DocumentFileName).HasMaxLength(255).IsRequired();
            entity.Property(e => e.DocumentContentType).HasMaxLength(120).IsRequired();
            entity.Property(e => e.DocumentHashSha256).HasMaxLength(64).IsRequired();
            entity.Property(e => e.AssignmentReason).HasMaxLength(1000);
            entity.Property(e => e.PageCount);
            entity.Property(e => e.DocumentSizeBytes);
            entity.Property(e => e.AssignedByUserId);
            entity.Property(e => e.AssignedAtUtc);
            entity.HasOne(e => e.IntegrationConnection).WithMany().HasForeignKey(e => e.IntegrationConnectionId).OnDelete(DeleteBehavior.Restrict);
            if (includeBaseNavigations)
            {
                entity.HasOne(e => e.AssignedPatient).WithMany().HasForeignKey(e => e.AssignedPatientId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.PatientDocument).WithMany().HasForeignKey(e => e.PatientDocumentId).OnDelete(DeleteBehavior.Restrict);
            }
            else
            {
                entity.HasOne(typeof(Patient).FullName!, navigationName: null).WithMany().HasForeignKey(nameof(InboundFax.AssignedPatientId)).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(typeof(PatientDocument).FullName!, navigationName: null).WithMany().HasForeignKey(nameof(InboundFax.PatientDocumentId)).OnDelete(DeleteBehavior.Restrict);
            }
        });

        modelBuilder.Entity<HepProgram>(entity =>
        {
            entity.ToTable("HepPrograms");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.PatientId, e.UpdatedAtUtc });
            entity.HasIndex(e => e.CurrentRevisionId);
            entity.HasIndex(e => e.ClinicId);
            entity.HasIndex(e => e.IntegrationConnectionId);
            entity.HasIndex(e => e.CreatedByUserId);
            entity.Property(e => e.ProviderProgramId).HasMaxLength(255);
            entity.Property(e => e.ProviderEpisodeId).HasMaxLength(255);
            entity.Property(e => e.LastFailureCode).HasMaxLength(160);
            entity.Property(e => e.Status);
            entity.Property(e => e.CreatedAtUtc);
            entity.Property(e => e.LastSyncedAtUtc);
            entity.Property(e => e.LastTrackingSyncAtUtc);
            if (includeBaseNavigations)
            {
                entity.HasOne(e => e.Clinic).WithMany().HasForeignKey(e => e.ClinicId).OnDelete(DeleteBehavior.Restrict);
            }
            else
            {
                entity.HasOne(typeof(Clinic).FullName!, navigationName: null).WithMany().HasForeignKey(nameof(HepProgram.ClinicId)).OnDelete(DeleteBehavior.Restrict);
            }
            entity.HasOne(e => e.IntegrationConnection).WithMany().HasForeignKey(e => e.IntegrationConnectionId).OnDelete(DeleteBehavior.Restrict);
            if (includeBaseNavigations)
            {
                entity.HasOne(e => e.Patient).WithMany().HasForeignKey(e => e.PatientId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.CreatedByUser).WithMany().HasForeignKey(e => e.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            }
            else
            {
                entity.HasOne(typeof(Patient).FullName!, navigationName: null).WithMany().HasForeignKey(nameof(HepProgram.PatientId)).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(typeof(User).FullName!, navigationName: null).WithMany().HasForeignKey(nameof(HepProgram.CreatedByUserId)).OnDelete(DeleteBehavior.Restrict);
            }
        });

        modelBuilder.Entity<HepProgramRevision>(entity =>
        {
            entity.ToTable("HepProgramRevisions");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.HepProgramId, e.Version }).IsUnique();
            entity.HasIndex(e => e.CreatedByUserId);
            entity.Property(e => e.Title).HasMaxLength(255).IsRequired();
            entity.Property(e => e.TherapistNotes).HasMaxLength(4000);
            entity.Property(e => e.ProviderVersion).HasMaxLength(255);
            entity.Property(e => e.Source);
            entity.Property(e => e.StartDate);
            entity.Property(e => e.EndDate);
            entity.Property(e => e.CreatedAtUtc);
            entity.Property(e => e.PublishedAtUtc);
            entity.HasOne(e => e.HepProgram).WithMany(e => e.Revisions).HasForeignKey(e => e.HepProgramId).OnDelete(DeleteBehavior.Cascade);
            if (includeBaseNavigations)
            {
                entity.HasOne(e => e.CreatedByUser).WithMany().HasForeignKey(e => e.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            }
            else
            {
                entity.HasOne(typeof(User).FullName!, navigationName: null).WithMany().HasForeignKey(nameof(HepProgramRevision.CreatedByUserId)).OnDelete(DeleteBehavior.Restrict);
            }
        });

        modelBuilder.Entity<HepPrescriptionExercise>(entity =>
        {
            entity.ToTable("HepPrescriptionExercises");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.HepProgramRevisionId, e.SortOrder }).IsUnique();
            entity.Property(e => e.ExternalExerciseId).HasMaxLength(255).IsRequired();
            entity.Property(e => e.Title).HasMaxLength(500).IsRequired();
            entity.Property(e => e.DescriptionOverride).HasMaxLength(4000);
            entity.Property(e => e.Sets).HasMaxLength(100);
            entity.Property(e => e.Repetitions).HasMaxLength(100);
            entity.Property(e => e.Weight).HasMaxLength(100);
            entity.Property(e => e.Frequency).HasMaxLength(200);
            entity.Property(e => e.Duration).HasMaxLength(100);
            entity.Property(e => e.Hold).HasMaxLength(100);
            entity.Property(e => e.Tempo).HasMaxLength(100);
            entity.Property(e => e.Rest).HasMaxLength(100);
            entity.Property(e => e.Level).HasMaxLength(100);
            entity.Property(e => e.Other).HasMaxLength(1000);
            entity.Property(e => e.IsHomeExercise);
            entity.Property(e => e.Mirror);
            entity.Property(e => e.Flip);
            entity.HasOne(e => e.HepProgramRevision).WithMany(e => e.Exercises).HasForeignKey(e => e.HepProgramRevisionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<HepTrackingObservation>(entity =>
        {
            entity.ToTable("HepTrackingObservations");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.HepProgramId, e.ProviderObservationId }).IsUnique();
            entity.HasIndex(e => new { e.HepProgramId, e.ActivityAtUtc });
            entity.Property(e => e.ProviderObservationId).HasMaxLength(255).IsRequired();
            entity.Property(e => e.ExternalExerciseId).HasMaxLength(255);
            entity.Property(e => e.Code).HasMaxLength(80).IsRequired();
            entity.Property(e => e.Value).HasMaxLength(255).IsRequired();
            entity.Property(e => e.UnitOfMeasure).HasMaxLength(80);
            entity.Property(e => e.ClinicId);
            entity.Property(e => e.ImportedAtUtc);
            entity.HasOne(e => e.HepProgram).WithMany(e => e.TrackingObservations).HasForeignKey(e => e.HepProgramId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    public static void ConfigureIntegrationSnapshotModels(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity(typeof(PatientDocument).FullName!, entity =>
            entity.Property<string>(nameof(PatientDocument.StorageKey)).HasMaxLength(1024));
        ConfigureIntegrationModels(modelBuilder, includeBaseNavigations: false);
    }

    /// <summary>
    /// Keeps the three provider snapshots aligned for the enterprise directory,
    /// insurance, and template aggregates without duplicating a large generated
    /// model block in every migrations assembly.
    /// </summary>
    public static void ConfigureEnterpriseDataSnapshotModels(ModelBuilder modelBuilder, string provider)
    {
        var isPostgres = provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase);
        var isSqlServer = provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase);
        var clinicEntityName = typeof(Clinic).FullName!;
        var clinicalNoteEntityName = typeof(ClinicalNote).FullName!;
        var patientEntityName = typeof(Patient).FullName!;
        var providerEntityName = typeof(ProviderDirectoryEntry).FullName!;
        var providerRelationshipEntityName = typeof(PatientProviderRelationship).FullName!;
        var policyEntityName = typeof(PatientInsurancePolicy).FullName!;
        var authorizationEntityName = typeof(PatientInsuranceAuthorization).FullName!;
        var templateEntityName = typeof(NoteTemplate).FullName!;
        var templateVersionEntityName = typeof(NoteTemplateVersion).FullName!;
        var npiFilter = isPostgres
            ? $"\"Npi\" IS NOT NULL AND \"IsArchived\" = FALSE AND \"Status\" = {(int)ProviderDirectoryStatus.Active}"
            : isSqlServer
                ? $"[Npi] IS NOT NULL AND [IsArchived] = 0 AND [Status] = {(int)ProviderDirectoryStatus.Active}"
                : $"Npi IS NOT NULL AND IsArchived = 0 AND Status = {(int)ProviderDirectoryStatus.Active}";
        var policyFilter = isPostgres
            ? $"\"IsArchived\" = FALSE AND \"Status\" = {(int)InsurancePolicyStatus.Active}"
            : isSqlServer
                ? $"[IsArchived] = 0 AND [Status] = {(int)InsurancePolicyStatus.Active}"
                : $"IsArchived = 0 AND Status = {(int)InsurancePolicyStatus.Active}";
        var activeTemplateFilter = isPostgres
            ? "\"IsArchived\" = FALSE"
            : isSqlServer
                ? "[IsArchived] = 0"
                : "IsArchived = 0";

        modelBuilder.Entity(providerEntityName, entity =>
        {
            entity.ToTable("ProviderDirectoryEntries");
            entity.Property<Guid>(nameof(ProviderDirectoryEntry.Id)).ValueGeneratedOnAdd();
            entity.Property<Guid?>(nameof(ProviderDirectoryEntry.ClinicId));
            entity.Property<string>(nameof(ProviderDirectoryEntry.FirstName)).HasMaxLength(100).IsRequired();
            entity.Property<string>(nameof(ProviderDirectoryEntry.LastName)).HasMaxLength(100).IsRequired();
            entity.Property<string>(nameof(ProviderDirectoryEntry.Credentials)).HasMaxLength(50);
            entity.Property<string>(nameof(ProviderDirectoryEntry.Npi)).HasMaxLength(10);
            entity.Property<string>(nameof(ProviderDirectoryEntry.Specialty)).HasMaxLength(150);
            entity.Property<string>(nameof(ProviderDirectoryEntry.TaxonomyCode)).HasMaxLength(20);
            entity.Property<string>(nameof(ProviderDirectoryEntry.OrganizationName)).HasMaxLength(200);
            entity.Property<string>(nameof(ProviderDirectoryEntry.Phone)).HasMaxLength(30);
            entity.Property<string>(nameof(ProviderDirectoryEntry.Fax)).HasMaxLength(30);
            entity.Property<string>(nameof(ProviderDirectoryEntry.Email)).HasMaxLength(255);
            entity.Property<string>(nameof(ProviderDirectoryEntry.AddressLine1)).HasMaxLength(200);
            entity.Property<string>(nameof(ProviderDirectoryEntry.AddressLine2)).HasMaxLength(200);
            entity.Property<string>(nameof(ProviderDirectoryEntry.City)).HasMaxLength(100);
            entity.Property<string>(nameof(ProviderDirectoryEntry.State)).HasMaxLength(100);
            entity.Property<string>(nameof(ProviderDirectoryEntry.ZipCode)).HasMaxLength(20);
            entity.Property<ProviderDirectoryStatus>(nameof(ProviderDirectoryEntry.Status));
            entity.Property<ProviderSubmissionSource>(nameof(ProviderDirectoryEntry.SubmissionSource));
            entity.Property<Guid?>(nameof(ProviderDirectoryEntry.SubmittedByUserId));
            entity.Property<Guid?>(nameof(ProviderDirectoryEntry.ReviewedByUserId));
            entity.Property<DateTime>(nameof(ProviderDirectoryEntry.SubmittedAtUtc));
            entity.Property<DateTime?>(nameof(ProviderDirectoryEntry.ReviewedAtUtc));
            entity.Property<string>(nameof(ProviderDirectoryEntry.ReviewReason)).HasMaxLength(500);
            entity.Property<bool>(nameof(ProviderDirectoryEntry.IsArchived));
            entity.Property<DateTime>(nameof(ProviderDirectoryEntry.LastModifiedUtc)).IsConcurrencyToken();
            entity.Property<Guid>(nameof(ProviderDirectoryEntry.ModifiedByUserId));
            entity.Property<SyncState>(nameof(ProviderDirectoryEntry.SyncState));
            entity.HasKey(nameof(ProviderDirectoryEntry.Id));
            entity.HasIndex(nameof(ProviderDirectoryEntry.ClinicId), nameof(ProviderDirectoryEntry.Status), nameof(ProviderDirectoryEntry.LastName), nameof(ProviderDirectoryEntry.FirstName));
            entity.HasIndex(nameof(ProviderDirectoryEntry.ClinicId), nameof(ProviderDirectoryEntry.Npi)).IsUnique().HasFilter(npiFilter);
            entity.HasOne(clinicEntityName, navigationName: null).WithMany().HasForeignKey(nameof(ProviderDirectoryEntry.ClinicId)).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity(providerRelationshipEntityName, entity =>
        {
            entity.ToTable("PatientProviderRelationships");
            entity.Property<Guid>(nameof(PatientProviderRelationship.Id)).ValueGeneratedOnAdd();
            entity.Property<Guid>(nameof(PatientProviderRelationship.PatientId));
            entity.Property<Guid>(nameof(PatientProviderRelationship.ProviderDirectoryEntryId));
            entity.Property<Guid?>(nameof(PatientProviderRelationship.ClinicId));
            entity.Property<PatientProviderRole>(nameof(PatientProviderRelationship.Role));
            entity.Property<DateTime?>(nameof(PatientProviderRelationship.EffectiveStartDate));
            entity.Property<DateTime?>(nameof(PatientProviderRelationship.EffectiveEndDate));
            entity.Property<bool>(nameof(PatientProviderRelationship.IsPrimary));
            entity.Property<bool>(nameof(PatientProviderRelationship.IsArchived));
            entity.Property<DateTime>(nameof(PatientProviderRelationship.LastModifiedUtc)).IsConcurrencyToken();
            entity.Property<Guid>(nameof(PatientProviderRelationship.ModifiedByUserId));
            entity.Property<SyncState>(nameof(PatientProviderRelationship.SyncState));
            entity.HasKey(nameof(PatientProviderRelationship.Id));
            entity.HasIndex(nameof(PatientProviderRelationship.PatientId), nameof(PatientProviderRelationship.Role), nameof(PatientProviderRelationship.IsArchived));
            entity.HasIndex(nameof(PatientProviderRelationship.ClinicId), nameof(PatientProviderRelationship.ProviderDirectoryEntryId));
            entity.HasOne(patientEntityName, navigationName: null).WithMany().HasForeignKey(nameof(PatientProviderRelationship.PatientId)).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(providerEntityName, navigationName: null).WithMany().HasForeignKey(nameof(PatientProviderRelationship.ProviderDirectoryEntryId)).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(clinicEntityName, navigationName: null).WithMany().HasForeignKey(nameof(PatientProviderRelationship.ClinicId)).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity(policyEntityName, entity =>
        {
            entity.ToTable("PatientInsurancePolicies");
            entity.Property<Guid>(nameof(PatientInsurancePolicy.Id)).ValueGeneratedOnAdd();
            entity.Property<Guid>(nameof(PatientInsurancePolicy.PatientId));
            entity.Property<Guid?>(nameof(PatientInsurancePolicy.ClinicId));
            entity.Property<InsuranceCoveragePriority>(nameof(PatientInsurancePolicy.CoveragePriority));
            entity.Property<string>(nameof(PatientInsurancePolicy.CarrierKey)).HasMaxLength(100);
            entity.Property<string>(nameof(PatientInsurancePolicy.CarrierDisplayName)).HasMaxLength(200);
            entity.Property<InsurancePayerType>(nameof(PatientInsurancePolicy.PayerType));
            entity.Property<string>(nameof(PatientInsurancePolicy.MemberOrPolicyNumber)).HasMaxLength(100);
            entity.Property<string>(nameof(PatientInsurancePolicy.GroupNumber)).HasMaxLength(100);
            entity.Property<DateTime?>(nameof(PatientInsurancePolicy.EffectiveStartDate));
            entity.Property<DateTime?>(nameof(PatientInsurancePolicy.EffectiveEndDate));
            entity.Property<InsurancePlanYearType>(nameof(PatientInsurancePolicy.PlanYearType));
            entity.Property<decimal?>(nameof(PatientInsurancePolicy.DeductibleAmount)).HasPrecision(18, 2);
            entity.Property<decimal?>(nameof(PatientInsurancePolicy.DeductibleMet)).HasPrecision(18, 2);
            entity.Property<decimal?>(nameof(PatientInsurancePolicy.OutOfPocketMaximum)).HasPrecision(18, 2);
            entity.Property<decimal?>(nameof(PatientInsurancePolicy.OutOfPocketMet)).HasPrecision(18, 2);
            entity.Property<decimal?>(nameof(PatientInsurancePolicy.CopayAmount)).HasPrecision(18, 2);
            entity.Property<decimal?>(nameof(PatientInsurancePolicy.CoinsurancePercent)).HasPrecision(5, 2);
            entity.Property<string>(nameof(PatientInsurancePolicy.AdjusterName)).HasMaxLength(150);
            entity.Property<string>(nameof(PatientInsurancePolicy.AdjusterPhone)).HasMaxLength(30);
            entity.Property<string>(nameof(PatientInsurancePolicy.AdjusterEmail)).HasMaxLength(255);
            entity.Property<string>(nameof(PatientInsurancePolicy.AdjusterFax)).HasMaxLength(30);
            entity.Property<InsurancePolicyStatus>(nameof(PatientInsurancePolicy.Status));
            entity.Property<bool>(nameof(PatientInsurancePolicy.IsArchived));
            entity.Property<DateTime>(nameof(PatientInsurancePolicy.LastModifiedUtc)).IsConcurrencyToken();
            entity.Property<Guid>(nameof(PatientInsurancePolicy.ModifiedByUserId));
            entity.Property<SyncState>(nameof(PatientInsurancePolicy.SyncState));
            entity.HasKey(nameof(PatientInsurancePolicy.Id));
            entity.HasIndex(nameof(PatientInsurancePolicy.PatientId), nameof(PatientInsurancePolicy.CoveragePriority)).IsUnique().HasDatabaseName("UX_PatientInsurancePolicies_PatientId_CoveragePriority_Active").HasFilter(policyFilter);
            entity.HasIndex(nameof(PatientInsurancePolicy.ClinicId), nameof(PatientInsurancePolicy.PatientId));
            entity.HasOne(patientEntityName, navigationName: null).WithMany().HasForeignKey(nameof(PatientInsurancePolicy.PatientId)).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(clinicEntityName, navigationName: null).WithMany().HasForeignKey(nameof(PatientInsurancePolicy.ClinicId)).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity(authorizationEntityName, entity =>
        {
            entity.ToTable("PatientInsuranceAuthorizations");
            entity.Property<Guid>(nameof(PatientInsuranceAuthorization.Id)).ValueGeneratedOnAdd();
            entity.Property<Guid>(nameof(PatientInsuranceAuthorization.PatientInsurancePolicyId));
            entity.Property<Guid>(nameof(PatientInsuranceAuthorization.PatientId));
            entity.Property<Guid?>(nameof(PatientInsuranceAuthorization.ClinicId));
            entity.Property<InsuranceAuthorizationType>(nameof(PatientInsuranceAuthorization.AuthorizationType));
            entity.Property<string>(nameof(PatientInsuranceAuthorization.ReferenceNumber)).HasMaxLength(100);
            entity.Property<InsuranceAuthorizationStatus>(nameof(PatientInsuranceAuthorization.Status));
            entity.Property<DateTime?>(nameof(PatientInsuranceAuthorization.ReceivedDate));
            entity.Property<DateTime?>(nameof(PatientInsuranceAuthorization.StartDate));
            entity.Property<DateTime?>(nameof(PatientInsuranceAuthorization.EndDate));
            entity.Property<decimal?>(nameof(PatientInsuranceAuthorization.AuthorizedUnits)).HasPrecision(18, 2);
            entity.Property<decimal?>(nameof(PatientInsuranceAuthorization.UsedUnits)).HasPrecision(18, 2);
            entity.Property<InsuranceVisitLimitPeriod>(nameof(PatientInsuranceAuthorization.VisitLimitPeriod));
            entity.Property<DateTime?>(nameof(PatientInsuranceAuthorization.ReauthorizationDueDate));
            entity.Property<int?>(nameof(PatientInsuranceAuthorization.VisitAlertThreshold));
            entity.Property<string>(nameof(PatientInsuranceAuthorization.Notes)).HasMaxLength(2000);
            entity.Property<bool>(nameof(PatientInsuranceAuthorization.IsArchived));
            entity.Property<DateTime>(nameof(PatientInsuranceAuthorization.LastModifiedUtc)).IsConcurrencyToken();
            entity.Property<Guid>(nameof(PatientInsuranceAuthorization.ModifiedByUserId));
            entity.Property<SyncState>(nameof(PatientInsuranceAuthorization.SyncState));
            entity.HasKey(nameof(PatientInsuranceAuthorization.Id));
            entity.HasIndex(nameof(PatientInsuranceAuthorization.PatientInsurancePolicyId), nameof(PatientInsuranceAuthorization.IsArchived));
            entity.HasIndex(nameof(PatientInsuranceAuthorization.ClinicId), nameof(PatientInsuranceAuthorization.PatientId));
            entity.HasOne(policyEntityName, navigationName: null).WithMany().HasForeignKey(nameof(PatientInsuranceAuthorization.PatientInsurancePolicyId)).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(patientEntityName, navigationName: null).WithMany().HasForeignKey(nameof(PatientInsuranceAuthorization.PatientId)).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(clinicEntityName, navigationName: null).WithMany().HasForeignKey(nameof(PatientInsuranceAuthorization.ClinicId)).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity(templateEntityName, entity =>
        {
            entity.ToTable("NoteTemplates");
            entity.Property<Guid>(nameof(NoteTemplate.Id)).ValueGeneratedOnAdd();
            entity.Property<Guid?>(nameof(NoteTemplate.ClinicId));
            entity.Property<NoteType>(nameof(NoteTemplate.NoteType));
            entity.Property<NoteTemplateVariant>(nameof(NoteTemplate.Variant));
            entity.Property<string>(nameof(NoteTemplate.Name)).HasMaxLength(150).IsRequired();
            entity.Property<Guid?>(nameof(NoteTemplate.ActiveVersionId));
            entity.Property<bool>(nameof(NoteTemplate.IsArchived));
            entity.Property<DateTime>(nameof(NoteTemplate.CreatedAtUtc));
            entity.Property<DateTime>(nameof(NoteTemplate.LastModifiedUtc)).IsConcurrencyToken();
            entity.Property<Guid>(nameof(NoteTemplate.CreatedByUserId));
            entity.Property<Guid>(nameof(NoteTemplate.ModifiedByUserId));
            entity.HasKey(nameof(NoteTemplate.Id));
            entity.HasIndex(nameof(NoteTemplate.ClinicId), nameof(NoteTemplate.NoteType), nameof(NoteTemplate.Variant)).IsUnique().HasDatabaseName("UX_NoteTemplates_ClinicId_NoteType_Variant_Active").HasFilter(activeTemplateFilter);
            entity.HasOne(clinicEntityName, navigationName: null).WithMany().HasForeignKey(nameof(NoteTemplate.ClinicId)).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(templateVersionEntityName, navigationName: null).WithMany().HasForeignKey(nameof(NoteTemplate.ActiveVersionId)).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity(templateVersionEntityName, entity =>
        {
            entity.ToTable("NoteTemplateVersions");
            entity.Property<Guid>(nameof(NoteTemplateVersion.Id)).ValueGeneratedOnAdd();
            entity.Property<Guid>(nameof(NoteTemplateVersion.NoteTemplateId));
            entity.Property<Guid?>(nameof(NoteTemplateVersion.ClinicId));
            entity.Property<int>(nameof(NoteTemplateVersion.VersionNumber));
            entity.Property<NoteTemplateVersionStatus>(nameof(NoteTemplateVersion.Status));
            entity.Property<string>(nameof(NoteTemplateVersion.SchemaJson)).IsRequired();
            entity.Property<Guid>(nameof(NoteTemplateVersion.CreatedByUserId));
            entity.Property<Guid?>(nameof(NoteTemplateVersion.SubmittedByUserId));
            entity.Property<Guid?>(nameof(NoteTemplateVersion.ReviewedByUserId));
            entity.Property<DateTime>(nameof(NoteTemplateVersion.CreatedAtUtc));
            entity.Property<DateTime>(nameof(NoteTemplateVersion.LastModifiedUtc)).IsConcurrencyToken();
            entity.Property<DateTime?>(nameof(NoteTemplateVersion.SubmittedAtUtc));
            entity.Property<DateTime?>(nameof(NoteTemplateVersion.PublishedAtUtc));
            entity.Property<DateTime?>(nameof(NoteTemplateVersion.RetiredAtUtc));
            entity.Property<string>(nameof(NoteTemplateVersion.ReviewComment)).HasMaxLength(1000);
            entity.HasKey(nameof(NoteTemplateVersion.Id));
            entity.HasIndex(nameof(NoteTemplateVersion.NoteTemplateId), nameof(NoteTemplateVersion.VersionNumber)).IsUnique();
            entity.HasIndex(nameof(NoteTemplateVersion.ClinicId), nameof(NoteTemplateVersion.Status));
            entity.HasOne(templateEntityName, navigationName: null).WithMany().HasForeignKey(nameof(NoteTemplateVersion.NoteTemplateId)).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(clinicEntityName, navigationName: null).WithMany().HasForeignKey(nameof(NoteTemplateVersion.ClinicId)).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity(clinicalNoteEntityName, entity =>
        {
            entity.Property<Guid?>(nameof(ClinicalNote.TemplateVersionId));
            entity.HasOne(templateVersionEntityName, navigationName: null).WithMany().HasForeignKey(nameof(ClinicalNote.TemplateVersionId)).OnDelete(DeleteBehavior.Restrict);
        });
    }

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

    private string ClinicalVisitOrdinalFilter()
    {
        if (Database.ProviderName?.Contains("Npgsql") == true)
        {
            return "\"ClinicalVisitOrdinal\" IS NOT NULL";
        }

        if (Database.IsSqlServer())
        {
            return "[ClinicalVisitOrdinal] IS NOT NULL";
        }

        return "ClinicalVisitOrdinal IS NOT NULL";
    }

    private string VisitTypeClinicCheckConstraint()
    {
        if (Database.ProviderName?.Contains("Npgsql") == true)
        {
            return "\"VisitTypeId\" IS NULL OR \"ClinicId\" IS NOT NULL";
        }

        if (Database.IsSqlServer())
        {
            return "[VisitTypeId] IS NULL OR [ClinicId] IS NOT NULL";
        }

        return "VisitTypeId IS NULL OR ClinicId IS NOT NULL";
    }

    private string ActiveInsurancePolicyFilter()
    {
        if (Database.ProviderName?.Contains("Npgsql") == true)
            return $"\"IsArchived\" = FALSE AND \"Status\" = {(int)InsurancePolicyStatus.Active}";
        return $"IsArchived = 0 AND Status = {(int)InsurancePolicyStatus.Active}";
    }

    private string ActiveProviderNpiFilter()
    {
        if (Database.ProviderName?.Contains("Npgsql") == true)
            return $"\"Npi\" IS NOT NULL AND \"IsArchived\" = FALSE AND \"Status\" = {(int)ProviderDirectoryStatus.Active}";
        return $"Npi IS NOT NULL AND IsArchived = 0 AND Status = {(int)ProviderDirectoryStatus.Active}";
    }

    private string ActiveNoteTemplateFilter()
    {
        if (Database.ProviderName?.Contains("Npgsql") == true)
            return "\"IsArchived\" = FALSE";
        return "IsArchived = 0";
    }
}

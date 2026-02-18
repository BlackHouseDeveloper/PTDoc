using Microsoft.EntityFrameworkCore;
using PTDoc.Core.Models;

namespace PTDoc.Infrastructure.Data;

/// <summary>
/// Application database context for PTDoc.
/// Supports both SQLite (local-first) and SQL Server (cloud) via provider configuration.
/// </summary>
public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    // Clinical entities
    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<ClinicalNote> ClinicalNotes => Set<ClinicalNote>();
    public DbSet<IntakeForm> IntakeForms => Set<IntakeForm>();

    // User & auth entities
    public DbSet<User> Users => Set<User>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<LoginAttempt> LoginAttempts => Set<LoginAttempt>();

    // System entities
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<SyncQueueItem> SyncQueueItems => Set<SyncQueueItem>();
    public DbSet<SyncConflictArchive> SyncConflictArchives => Set<SyncConflictArchive>();
    public DbSet<ExternalSystemMapping> ExternalSystemMappings => Set<ExternalSystemMapping>();
    public DbSet<Addendum> Addendums => Set<Addendum>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure Patient
        modelBuilder.Entity<Patient>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.LastModifiedUtc);
            entity.HasIndex(e => new { e.FirstName, e.LastName });
            entity.HasIndex(e => e.MedicalRecordNumber).IsUnique().HasFilter("MedicalRecordNumber IS NOT NULL");
            entity.HasIndex(e => e.Email).HasFilter("Email IS NOT NULL");

            entity.Property(e => e.FirstName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.LastName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Email).HasMaxLength(255);
            entity.Property(e => e.Phone).HasMaxLength(20);
            entity.Property(e => e.MedicalRecordNumber).HasMaxLength(50);

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
            entity.HasIndex(e => e.SignedUtc);
            entity.HasIndex(e => e.LastModifiedUtc);

            entity.Property(e => e.SignatureHash).HasMaxLength(64); // SHA-256 hex string

            // Relationship to Appointment (optional)
            entity.HasOne(e => e.Appointment)
                .WithMany()
                .HasForeignKey(e => e.AppointmentId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Configure IntakeForm
        modelBuilder.Entity<IntakeForm>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.PatientId);
            entity.HasIndex(e => e.AccessToken).IsUnique();
            entity.HasIndex(e => e.LastModifiedUtc);

            entity.Property(e => e.TemplateVersion).HasMaxLength(50).IsRequired();
            entity.Property(e => e.AccessToken).HasMaxLength(256).IsRequired();
        });

        // Configure User
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Username).IsUnique();
            entity.HasIndex(e => e.Email).IsUnique().HasFilter("Email IS NOT NULL");
            entity.HasIndex(e => e.IsActive);

            entity.Property(e => e.Username).HasMaxLength(100).IsRequired();
            entity.Property(e => e.PinHash).HasMaxLength(256).IsRequired();
            entity.Property(e => e.FirstName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.LastName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Email).HasMaxLength(255);
            entity.Property(e => e.Role).HasMaxLength(50).IsRequired();
            entity.Property(e => e.LicenseNumber).HasMaxLength(50);
            entity.Property(e => e.LicenseState).HasMaxLength(2);

            // Relationships
            entity.HasMany(e => e.Sessions)
                .WithOne(e => e.User)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
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
            entity.HasIndex(e => e.UserId).HasFilter("UserId IS NOT NULL");
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
            entity.HasIndex(e => e.UserId).HasFilter("UserId IS NOT NULL");
            entity.HasIndex(e => e.CorrelationId);
            entity.HasIndex(e => new { e.EntityType, e.EntityId }).HasFilter("EntityType IS NOT NULL AND EntityId IS NOT NULL");

            entity.Property(e => e.EventType).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Severity).HasMaxLength(20).IsRequired();
            entity.Property(e => e.EntityType).HasMaxLength(100);
            entity.Property(e => e.CorrelationId).HasMaxLength(100).IsRequired();
            entity.Property(e => e.ErrorMessage).HasMaxLength(2000);
        });

        // Configure SyncQueueItem
        modelBuilder.Entity<SyncQueueItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Status);
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

        // Configure Addendum
        modelBuilder.Entity<Addendum>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ClinicalNoteId);
            entity.HasIndex(e => e.CreatedUtc);
            entity.HasIndex(e => e.CreatedByUserId);

            entity.Property(e => e.Content).IsRequired();

            // Relationship to ClinicalNote
            entity.HasOne(e => e.ClinicalNote)
                .WithMany()
                .HasForeignKey(e => e.ClinicalNoteId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}

using Microsoft.EntityFrameworkCore;
using PTDoc.Application.LocalData.Entities;

namespace PTDoc.Infrastructure.LocalData;

/// <summary>
/// EF Core database context for the MAUI client's local encrypted SQLite database.
/// This context is separate from <c>ApplicationDbContext</c> (server-side) and stores only
/// the lightweight cached data needed for offline operation.
/// </summary>
public class LocalDbContext : DbContext
{
    public LocalDbContext(DbContextOptions<LocalDbContext> options)
        : base(options)
    {
    }

    public DbSet<LocalUserProfile> UserProfiles => Set<LocalUserProfile>();
    public DbSet<LocalPatientSummary> PatientSummaries => Set<LocalPatientSummary>();
    public DbSet<LocalAppointmentSummary> AppointmentSummaries => Set<LocalAppointmentSummary>();
    public DbSet<LocalIntakeFormDraft> IntakeFormDrafts => Set<LocalIntakeFormDraft>();
    public DbSet<LocalClinicalNoteDraft> ClinicalNoteDrafts => Set<LocalClinicalNoteDraft>();
    public DbSet<LocalSyncQueueItem> SyncQueueItems => Set<LocalSyncQueueItem>();
    public DbSet<LocalSyncMetadata> SyncMetadata => Set<LocalSyncMetadata>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // LocalUserProfile
        modelBuilder.Entity<LocalUserProfile>(entity =>
        {
            entity.HasKey(e => e.LocalId);
            entity.Property(e => e.LocalId).ValueGeneratedOnAdd();
            entity.HasIndex(e => e.ServerId);
            entity.HasIndex(e => e.SyncState);

            entity.Property(e => e.Username).HasMaxLength(100).IsRequired();
            entity.Property(e => e.FirstName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.LastName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Email).HasMaxLength(255);
            entity.Property(e => e.Role).HasMaxLength(50).IsRequired();
        });

        // LocalPatientSummary
        modelBuilder.Entity<LocalPatientSummary>(entity =>
        {
            entity.HasKey(e => e.LocalId);
            entity.Property(e => e.LocalId).ValueGeneratedOnAdd();
            entity.HasIndex(e => e.ServerId);
            entity.HasIndex(e => e.SyncState);
            entity.HasIndex(e => new { e.LastName, e.FirstName });

            entity.Property(e => e.FirstName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.LastName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.MedicalRecordNumber).HasMaxLength(50);
            entity.Property(e => e.Phone).HasMaxLength(20);
            entity.Property(e => e.Email).HasMaxLength(255);
        });

        // LocalAppointmentSummary
        modelBuilder.Entity<LocalAppointmentSummary>(entity =>
        {
            entity.HasKey(e => e.LocalId);
            entity.Property(e => e.LocalId).ValueGeneratedOnAdd();
            entity.HasIndex(e => e.ServerId);
            entity.HasIndex(e => e.PatientServerId);
            entity.HasIndex(e => e.StartTimeUtc);
            entity.HasIndex(e => e.SyncState);

            entity.Property(e => e.PatientFirstName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.PatientLastName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(50);
            entity.Property(e => e.Notes).HasMaxLength(1000);
        });

        // LocalIntakeFormDraft
        modelBuilder.Entity<LocalIntakeFormDraft>(entity =>
        {
            entity.HasKey(e => e.LocalId);
            entity.Property(e => e.LocalId).ValueGeneratedOnAdd();
            entity.HasIndex(e => e.ServerId);
            entity.HasIndex(e => e.PatientServerId);
            entity.HasIndex(e => e.SyncState);
            entity.HasIndex(e => e.IsLocked);

            entity.Property(e => e.TemplateVersion).HasMaxLength(50).IsRequired();
        });

        // LocalClinicalNoteDraft
        modelBuilder.Entity<LocalClinicalNoteDraft>(entity =>
        {
            entity.HasKey(e => e.LocalId);
            entity.Property(e => e.LocalId).ValueGeneratedOnAdd();
            entity.HasIndex(e => e.ServerId);
            entity.HasIndex(e => e.PatientServerId);
            entity.HasIndex(e => e.SyncState);
            entity.HasIndex(e => e.DateOfService);

            entity.Property(e => e.NoteType).HasMaxLength(50).IsRequired();
            entity.Property(e => e.SignatureHash).HasMaxLength(500);
        });

        // LocalSyncQueueItem
        modelBuilder.Entity<LocalSyncQueueItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.OperationId).IsUnique();
            entity.HasIndex(e => new { e.Status, e.CreatedUtc });
            entity.HasIndex(e => new { e.EntityType, e.LocalEntityId, e.Status });

            entity.Property(e => e.EntityType).HasMaxLength(100).IsRequired();
            entity.Property(e => e.PayloadJson).IsRequired();
            entity.Property(e => e.ErrorMessage).HasMaxLength(2000);
        });

        // LocalSyncMetadata
        modelBuilder.Entity<LocalSyncMetadata>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.HasIndex(e => e.EntityType).IsUnique();

            entity.Property(e => e.EntityType).HasMaxLength(100).IsRequired();
            entity.Property(e => e.SyncToken).HasMaxLength(500);
        });
    }
}

using PTDoc.Application.LocalData;
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
    private readonly LocalDataScope? scope;
    public bool ScopeEnabled => scope is not null;
    public string? CurrentScopeKey => scope?.Key;

    public LocalDbContext(DbContextOptions<LocalDbContext> options, LocalDataScope? scope = null)
        : base(options)
    {
        this.scope = scope;
    }

    public async Task<IDisposable?> EnterScopeAsync(CancellationToken cancellationToken = default)
        => scope is null ? null : await scope.EnterAsync(cancellationToken);

    private void StampScope()
    {
        if (scope is null) return;
        var key = scope.Key ?? throw new InvalidOperationException("No clinic is selected for offline data.");
        foreach (var entry in ChangeTracker.Entries().Where(item => item.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            var property = entry.Property("ScopeKey");
            if (entry.State == EntityState.Added && property.CurrentValue is null) property.CurrentValue = key;
            if (!Equals(property.CurrentValue, key)
                || (entry.State != EntityState.Added && !Equals(property.OriginalValue, key)))
                throw new InvalidOperationException("Offline data belongs to another clinic or requires legacy recovery.");
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampScope();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampScope();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
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
        modelBuilder.Entity<LocalUserProfile>().Property(item => item.ScopeKey).HasMaxLength(65);
        modelBuilder.Entity<LocalUserProfile>().HasQueryFilter(item => !ScopeEnabled || (CurrentScopeKey != null && item.ScopeKey == CurrentScopeKey));
        modelBuilder.Entity<LocalPatientSummary>().Property(item => item.ScopeKey).HasMaxLength(65);
        modelBuilder.Entity<LocalPatientSummary>().HasQueryFilter(item => !ScopeEnabled || (CurrentScopeKey != null && item.ScopeKey == CurrentScopeKey));
        modelBuilder.Entity<LocalAppointmentSummary>().Property(item => item.ScopeKey).HasMaxLength(65);
        modelBuilder.Entity<LocalAppointmentSummary>().HasQueryFilter(item => !ScopeEnabled || (CurrentScopeKey != null && item.ScopeKey == CurrentScopeKey));
        modelBuilder.Entity<LocalIntakeFormDraft>().Property(item => item.ScopeKey).HasMaxLength(65);
        modelBuilder.Entity<LocalIntakeFormDraft>().HasQueryFilter(item => !ScopeEnabled || (CurrentScopeKey != null && item.ScopeKey == CurrentScopeKey));
        modelBuilder.Entity<LocalClinicalNoteDraft>().Property(item => item.ScopeKey).HasMaxLength(65);
        modelBuilder.Entity<LocalClinicalNoteDraft>().HasQueryFilter(item => !ScopeEnabled || (CurrentScopeKey != null && item.ScopeKey == CurrentScopeKey));
        modelBuilder.Entity<LocalSyncQueueItem>().Property(item => item.ScopeKey).HasMaxLength(65);
        modelBuilder.Entity<LocalSyncQueueItem>().HasQueryFilter(item => !ScopeEnabled || (CurrentScopeKey != null && item.ScopeKey == CurrentScopeKey));
        modelBuilder.Entity<LocalSyncMetadata>().Property(item => item.ScopeKey).HasMaxLength(65);
        modelBuilder.Entity<LocalSyncMetadata>().HasQueryFilter(item => !ScopeEnabled || (CurrentScopeKey != null && item.ScopeKey == CurrentScopeKey));

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
            entity.HasIndex(e => e.ReviewedAtUtc);

            entity.Property(e => e.TemplateVersion).HasMaxLength(50).IsRequired();
        });

        // LocalClinicalNoteDraft
        modelBuilder.Entity<LocalClinicalNoteDraft>(entity =>
        {
            entity.HasKey(e => e.LocalId);
            entity.Property(e => e.LocalId).ValueGeneratedOnAdd();
            entity.HasIndex(e => e.ServerId);
            entity.HasIndex(e => e.PatientServerId);
            entity.HasIndex(e => e.ParentNoteId);
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
            entity.HasIndex(e => new { e.ScopeKey, e.EntityType }).IsUnique();

            entity.Property(e => e.EntityType).HasMaxLength(100).IsRequired();
            entity.Property(e => e.SyncToken).HasMaxLength(500);
        });
    }
}

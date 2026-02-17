using Microsoft.EntityFrameworkCore;
using PTDoc.Core.Models;
using PTDoc.Core.Interfaces;

namespace PTDoc.Infrastructure.Data;

/// <summary>
/// Entity Framework Core database context for PTDoc.
/// Supports both SQLite (offline-first) and SQL Server (Azure future).
/// </summary>
public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    // User management
    public DbSet<User> Users => Set<User>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    
    // Patient management
    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<ExternalSystemMapping> ExternalSystemMappings => Set<ExternalSystemMapping>();
    
    // Appointments
    public DbSet<Appointment> Appointments => Set<Appointment>();
    
    // Clinical documentation
    public DbSet<ClinicalNote> ClinicalNotes => Set<ClinicalNote>();
    
    // Intake
    public DbSet<IntakeTemplate> IntakeTemplates => Set<IntakeTemplate>();
    public DbSet<IntakeForm> IntakeForms => Set<IntakeForm>();
    
    // Sync
    public DbSet<SyncQueueItem> SyncQueueItems => Set<SyncQueueItem>();
    
    // Audit
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // User configuration
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Username).IsUnique();
            entity.HasIndex(e => e.Email);
            entity.Property(e => e.Username).IsRequired().HasMaxLength(100);
            entity.Property(e => e.FirstName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.LastName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Email).IsRequired().HasMaxLength(200);
        });

        // UserSession configuration
        modelBuilder.Entity<UserSession>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.SessionToken).IsUnique();
            entity.HasIndex(e => e.UserId);
            entity.HasOne(e => e.User)
                  .WithMany()
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // Patient configuration
        modelBuilder.Entity<Patient>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.MedicalRecordNumber).IsUnique();
            entity.HasIndex(e => new { e.FirstName, e.LastName, e.DateOfBirth });
            entity.HasIndex(e => e.Email);
            entity.HasIndex(e => e.PhoneNumber);
            
            // Soft delete query filter
            entity.HasQueryFilter(p => !p.IsDeleted);
            
            // Self-referencing relationship for patient merging
            entity.HasOne(e => e.MergedIntoPatient)
                  .WithMany()
                  .HasForeignKey(e => e.MergedIntoPatientId)
                  .OnDelete(DeleteBehavior.Restrict);
                  
            // Primary therapist relationship
            entity.HasOne(e => e.PrimaryTherapist)
                  .WithMany()
                  .HasForeignKey(e => e.PrimaryTherapistId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        // ExternalSystemMapping configuration
        modelBuilder.Entity<ExternalSystemMapping>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ExternalSystemName, e.ExternalId }).IsUnique();
            entity.HasIndex(e => e.PatientId);
            
            entity.HasOne(e => e.Patient)
                  .WithMany(p => p.ExternalSystemMappings)
                  .HasForeignKey(e => e.PatientId)
                  .OnDelete(DeleteBehavior.Cascade);
                  
            entity.Property(e => e.ExternalSystemName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.ExternalId).IsRequired().HasMaxLength(200);
        });

        // Appointment configuration
        modelBuilder.Entity<Appointment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.PatientId);
            entity.HasIndex(e => e.ClinicianId);
            entity.HasIndex(e => e.ScheduledStartUtc);
            entity.HasIndex(e => e.Status);
            
            entity.HasOne(e => e.Patient)
                  .WithMany(p => p.Appointments)
                  .HasForeignKey(e => e.PatientId)
                  .OnDelete(DeleteBehavior.Restrict);
                  
            entity.HasOne(e => e.Clinician)
                  .WithMany()
                  .HasForeignKey(e => e.ClinicianId)
                  .OnDelete(DeleteBehavior.Restrict);
                  
            entity.HasOne(e => e.ClinicalNote)
                  .WithOne(n => n.Appointment)
                  .HasForeignKey<Appointment>(e => e.ClinicalNoteId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        // ClinicalNote configuration
        modelBuilder.Entity<ClinicalNote>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.PatientId);
            entity.HasIndex(e => e.AuthorId);
            entity.HasIndex(e => e.DateOfService);
            entity.HasIndex(e => e.NoteType);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.SignedUtc);
            
            entity.HasOne(e => e.Patient)
                  .WithMany(p => p.ClinicalNotes)
                  .HasForeignKey(e => e.PatientId)
                  .OnDelete(DeleteBehavior.Restrict);
                  
            entity.HasOne(e => e.Author)
                  .WithMany()
                  .HasForeignKey(e => e.AuthorId)
                  .OnDelete(DeleteBehavior.Restrict);
                  
            entity.HasOne(e => e.CoSignedBy)
                  .WithMany()
                  .HasForeignKey(e => e.CoSignedByUserId)
                  .OnDelete(DeleteBehavior.Restrict);
                  
            // Addendum relationship
            entity.HasOne(e => e.OriginalNote)
                  .WithMany(n => n.Addenda)
                  .HasForeignKey(e => e.OriginalNoteId)
                  .OnDelete(DeleteBehavior.Restrict);
                  
            entity.Property(e => e.SignatureHash).HasMaxLength(64); // SHA-256 = 64 hex chars
        });

        // IntakeTemplate configuration
        modelBuilder.Entity<IntakeTemplate>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.Name, e.Version }).IsUnique();
            entity.HasIndex(e => e.IsActive);
            
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
        });

        // IntakeForm configuration
        modelBuilder.Entity<IntakeForm>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.PatientId);
            entity.HasIndex(e => e.IntakeTemplateId);
            entity.HasIndex(e => e.AccessTokenHash);
            entity.HasIndex(e => e.Status);
            
            entity.HasOne(e => e.Patient)
                  .WithMany(p => p.IntakeForms)
                  .HasForeignKey(e => e.PatientId)
                  .OnDelete(DeleteBehavior.Cascade);
                  
            entity.HasOne(e => e.Template)
                  .WithMany(t => t.IntakeForms)
                  .HasForeignKey(e => e.IntakeTemplateId)
                  .OnDelete(DeleteBehavior.Restrict);
                  
            entity.HasOne(e => e.EvaluationNote)
                  .WithMany()
                  .HasForeignKey(e => e.EvaluationNoteId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        // SyncQueueItem configuration
        modelBuilder.Entity<SyncQueueItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.EntityType, e.EntityId });
            entity.HasIndex(e => e.Priority);
            entity.HasIndex(e => e.IsProcessed);
            entity.HasIndex(e => e.HasConflict);
            
            entity.Property(e => e.EntityType).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Operation).IsRequired().HasMaxLength(50);
        });

        // AuditLog configuration
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.EventTimestampUtc);
            entity.HasIndex(e => e.EventCategory);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => new { e.EntityType, e.EntityId });
            
            entity.Property(e => e.EventType).IsRequired().HasMaxLength(100);
            entity.Property(e => e.EventCategory).IsRequired().HasMaxLength(50);
        });

        // Apply sync state conventions to all ISyncTrackedEntity types
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ISyncTrackedEntity).IsAssignableFrom(entityType.ClrType))
            {
                modelBuilder.Entity(entityType.ClrType)
                    .Property(nameof(ISyncTrackedEntity.LastModifiedUtc))
                    .IsRequired();
                    
                modelBuilder.Entity(entityType.ClrType)
                    .HasIndex(nameof(ISyncTrackedEntity.SyncState));
            }
        }
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Automatically update LastModifiedUtc for all sync-tracked entities
        UpdateSyncTrackedEntities();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        UpdateSyncTrackedEntities();
        return base.SaveChanges();
    }

    private void UpdateSyncTrackedEntities()
    {
        var entries = ChangeTracker.Entries<ISyncTrackedEntity>()
            .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified);

        var now = DateTime.UtcNow;
        
        foreach (var entry in entries)
        {
            entry.Entity.LastModifiedUtc = now;
            
            // Set sync state to Pending for modified entities
            if (entry.State == EntityState.Modified)
            {
                entry.Entity.SyncState = Core.Enums.SyncState.Pending;
            }
            
            // For added entities, sync state is already set in the entity constructor
        }
    }
}

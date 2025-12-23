using Microsoft.EntityFrameworkCore;
using PTDoc.Models;

namespace PTDoc.Data;

/// <summary>
/// Entity Framework database context for the PTDoc application.
/// </summary>
public class PTDocDbContext : DbContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PTDocDbContext"/> class.
    /// </summary>
    /// <param name="options">Database context options.</param>
    public PTDocDbContext(DbContextOptions<PTDocDbContext> options) : base(options)
    {
    }

    /// <summary>
    /// Gets the patients entity set.
    /// </summary>
    public DbSet<Patient> Patients => Set<Patient>();

    /// <summary>
    /// Gets the SOAP notes entity set.
    /// </summary>
    public DbSet<SOAPNote> SOAPNotes => Set<SOAPNote>();

    /// <summary>
    /// Gets the insurances entity set.
    /// </summary>
    public DbSet<Insurance> Insurances => Set<Insurance>();

    /// <summary>
    /// Gets the app states entity set.
    /// </summary>
    public DbSet<AppState> AppStates => Set<AppState>();

    /// <inheritdoc/>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        
        foreach (var entry in ChangeTracker.Entries<Entity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
            }

            if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }

        // Update AppState timestamps separately since it doesn't inherit from Entity
        foreach (var entry in ChangeTracker.Entries<AppState>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
            }

            if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Global soft-delete filter for entities
        modelBuilder.Entity<Patient>().HasQueryFilter(p => !p.IsDeleted);
        modelBuilder.Entity<SOAPNote>().HasQueryFilter(s => !s.IsDeleted);
        modelBuilder.Entity<Insurance>().HasQueryFilter(i => !i.IsDeleted);

        // Configure Patient
        modelBuilder.Entity<Patient>(entity =>
        {
            entity.Property(p => p.FirstName).HasMaxLength(60).IsRequired();
            entity.Property(p => p.LastName).HasMaxLength(60).IsRequired();
            entity.Property(p => p.Email).HasMaxLength(120);
            entity.Property(p => p.PhoneNumber).HasMaxLength(30);
            entity.Property(p => p.MRN).HasMaxLength(40);
            entity.Property(p => p.Sex).HasMaxLength(20);
            entity.Property(p => p.Address).HasMaxLength(200);
            entity.Property(p => p.City).HasMaxLength(100);
            entity.Property(p => p.State).HasMaxLength(50);
            entity.Property(p => p.ZipCode).HasMaxLength(20);

            entity.HasIndex(e => new { e.LastName, e.FirstName });
            entity.HasIndex(e => new { e.UpdatedAt, e.CreatedAt });
            entity.HasIndex(e => e.Email);
            entity.HasIndex(e => e.IsDeleted);
        });

        // Configure SOAPNote
        modelBuilder.Entity<SOAPNote>(entity =>
        {
            entity.Property(s => s.Subjective).HasMaxLength(4000);
            entity.Property(s => s.Objective).HasMaxLength(4000);
            entity.Property(s => s.Assessment).HasMaxLength(4000);
            entity.Property(s => s.Plan).HasMaxLength(4000);
            entity.Property(s => s.DiagnosisCode).HasMaxLength(20);
            entity.Property(s => s.TreatmentCode).HasMaxLength(20);

            entity.HasIndex(e => e.PatientId);
            entity.HasIndex(e => e.VisitDate);
            entity.HasIndex(e => e.CreatedAt);

            entity.HasOne(s => s.Patient)
                  .WithMany(p => p.SOAPNotes)
                  .HasForeignKey(s => s.PatientId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure Insurance
        modelBuilder.Entity<Insurance>(entity =>
        {
            entity.Property(i => i.ProviderName).HasMaxLength(200).IsRequired();
            entity.Property(i => i.PolicyNumber).HasMaxLength(100).IsRequired();
            entity.Property(i => i.GroupNumber).HasMaxLength(100);
            entity.Property(i => i.SubscriberName).HasMaxLength(200);
            entity.Property(i => i.InsuranceType).HasMaxLength(50);

            entity.HasIndex(e => e.PatientId);
            entity.HasIndex(e => e.PolicyNumber);
            entity.HasIndex(e => e.IsDeleted);

            entity.HasOne(i => i.Patient)
                  .WithMany(p => p.Insurances)
                  .HasForeignKey(i => i.PatientId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure AppState
        modelBuilder.Entity<AppState>(entity =>
        {
            entity.Property(a => a.Key).HasMaxLength(100).IsRequired();
            entity.Property(a => a.Value).HasMaxLength(4000);
            entity.Property(a => a.Description).HasMaxLength(500);

            entity.HasIndex(e => e.Key).IsUnique();
        });
    }
}

using Microsoft.EntityFrameworkCore;
using PTDoc.Models;
using PTDoc.Services;

namespace PTDoc.Data;

/// <summary>
/// Entity Framework database context for the PTDoc application.
/// </summary>
public class PTDocDbContext : DbContext
{
    private readonly ITenantContext? _tenantContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="PTDocDbContext"/> class.
    /// </summary>
    /// <param name="options">Database context options.</param>
    /// <param name="tenantContext">
    /// Tenant context injected via DI.  When provided with a non-empty <c>ClinicId</c>,
    /// per-clinic query filters and write-time tenant enforcement are both active.
    /// Pass <c>null</c> (or a context with <c>ClinicId == Guid.Empty</c>) only in
    /// design-time / migration / system-administrative scenarios where full schema
    /// access is explicitly required.
    /// </param>
    public PTDocDbContext(DbContextOptions<PTDocDbContext> options, ITenantContext? tenantContext = null)
        : base(options)
    {
        _tenantContext = tenantContext;
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

    /// <summary>
    /// Gets the audit logs entity set.
    /// </summary>
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    /// <summary>
    /// Returns the current clinic identifier from the tenant context.
    /// Returns <c>Guid.Empty</c> when no tenant context is active.
    /// <para>
    /// <b>Security note:</b> <c>Guid.Empty</c> is treated as an explicit bypass that disables the
    /// per-clinic filter. This is intentional for system/design-time/test contexts where full access
    /// is required. In production, every request handler <em>must</em> set a non-Empty ClinicId on
    /// the <see cref="ITenantContext"/> before performing any data access.  The filter therefore has
    /// <em>no</em> <c>ClinicId == null</c> path – the only bypass is via the Guid.Empty sentinel.
    /// </para>
    /// </summary>
    private Guid CurrentClinicId => _tenantContext?.ClinicId ?? Guid.Empty;

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

        // Enforce tenant ownership on all tenant-scoped writes when a clinic is active.
        // Added entities have their ClinicId stamped with CurrentClinicId.
        // Modified entities whose ClinicId does not match CurrentClinicId are rejected.
        EnforceTenantOwnership();

        return base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Stamps <c>ClinicId</c> on new tenant-scoped entities and rejects modifications
    /// that would move an entity into a different clinic's partition.
    /// This is a no-op when <c>CurrentClinicId == Guid.Empty</c> (system/design-time context).
    /// </summary>
    private void EnforceTenantOwnership()
    {
        if (CurrentClinicId == Guid.Empty) return;

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State != EntityState.Added && entry.State != EntityState.Modified) continue;

            switch (entry.Entity)
            {
                case Patient patient:
                    if (entry.State == EntityState.Added)
                        patient.ClinicId = CurrentClinicId;
                    else if (patient.ClinicId != CurrentClinicId)
                        throw new InvalidOperationException(
                            $"Cross-tenant write rejected: Patient {patient.Id} belongs to a different clinic.");
                    break;

                case SOAPNote note:
                    if (entry.State == EntityState.Added)
                        note.ClinicId = CurrentClinicId;
                    else if (note.ClinicId != CurrentClinicId)
                        throw new InvalidOperationException(
                            $"Cross-tenant write rejected: SOAPNote {note.Id} belongs to a different clinic.");
                    break;

                case Insurance insurance:
                    if (entry.State == EntityState.Added)
                        insurance.ClinicId = CurrentClinicId;
                    else if (insurance.ClinicId != CurrentClinicId)
                        throw new InvalidOperationException(
                            $"Cross-tenant write rejected: Insurance {insurance.Id} belongs to a different clinic.");
                    break;

                case AuditLog auditLog:
                    if (entry.State == EntityState.Added)
                        auditLog.ClinicId = CurrentClinicId;
                    else if (auditLog.ClinicId != CurrentClinicId)
                        throw new InvalidOperationException(
                            $"Cross-tenant write rejected: AuditLog {auditLog.Id} belongs to a different clinic.");
                    break;
            }
        }
    }

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // -----------------------------------------------------------------------
        // Global filters: soft-delete + strict tenant isolation.
        // The ClinicId filter uses CurrentClinicId which safely returns Guid.Empty
        // when no tenant context is set. Guid.Empty is treated as "bypass" so that
        // design-time and admin contexts work without a tenant. All other contexts
        // must have a non-Empty ClinicId set – the "== null" bypass path is absent.
        // -----------------------------------------------------------------------
        modelBuilder.Entity<Patient>().HasQueryFilter(p =>
            !p.IsDeleted &&
            (CurrentClinicId == Guid.Empty || p.ClinicId == CurrentClinicId));

        modelBuilder.Entity<SOAPNote>().HasQueryFilter(s =>
            !s.IsDeleted &&
            (CurrentClinicId == Guid.Empty || s.ClinicId == CurrentClinicId));

        modelBuilder.Entity<Insurance>().HasQueryFilter(i =>
            !i.IsDeleted &&
            (CurrentClinicId == Guid.Empty || i.ClinicId == CurrentClinicId));

        // AuditLog is tenant-scoped: each clinic sees only its own audit entries.
        modelBuilder.Entity<AuditLog>().HasQueryFilter(a =>
            CurrentClinicId == Guid.Empty || a.ClinicId == CurrentClinicId);

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
            entity.HasIndex(e => e.ClinicId);
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
            entity.Property(s => s.SignedBy).HasMaxLength(256);

            entity.HasIndex(e => e.PatientId);
            entity.HasIndex(e => e.VisitDate);
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.ClinicId);

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
            entity.HasIndex(e => e.ClinicId);

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

        // Configure AuditLog
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.EventType).HasMaxLength(100).IsRequired();
            entity.Property(e => e.EntityType).HasMaxLength(100);
            entity.Property(e => e.UserId).HasMaxLength(256);
            entity.Property(e => e.Details).HasMaxLength(2000);

            entity.HasIndex(e => e.TimestampUtc);
            entity.HasIndex(e => e.ClinicId);
            entity.HasIndex(e => new { e.EntityType, e.EntityId });
        });
    }
}

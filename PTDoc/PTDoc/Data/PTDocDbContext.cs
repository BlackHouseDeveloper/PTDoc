using Microsoft.EntityFrameworkCore;
using PTDoc.Models;

namespace PTDoc.Data;

public class PTDocDbContext : DbContext
{
    public PTDocDbContext(DbContextOptions<PTDocDbContext> options) : base(options)
    {
    }

    public DbSet<Patient> Patients { get; set; }
    public DbSet<SOAPNote> SOAPNotes { get; set; }
    public DbSet<Insurance> Insurances { get; set; }
    public DbSet<AppState> AppStates { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure Patient
        modelBuilder.Entity<Patient>(entity =>
        {
            entity.HasIndex(e => e.LastName);
            entity.HasIndex(e => e.Email);
            entity.HasIndex(e => e.IsActive);
        });

        // Configure SOAPNote
        modelBuilder.Entity<SOAPNote>(entity =>
        {
            entity.HasIndex(e => e.PatientId);
            entity.HasIndex(e => e.VisitDate);
            entity.HasIndex(e => e.CreatedDate);

            entity.HasOne(s => s.Patient)
                  .WithMany(p => p.SOAPNotes)
                  .HasForeignKey(s => s.PatientId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure Insurance
        modelBuilder.Entity<Insurance>(entity =>
        {
            entity.HasIndex(e => e.PatientId);
            entity.HasIndex(e => e.PolicyNumber);
            entity.HasIndex(e => e.IsActive);

            entity.HasOne(i => i.Patient)
                  .WithMany(p => p.Insurances)
                  .HasForeignKey(i => i.PatientId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure AppState
        modelBuilder.Entity<AppState>(entity =>
        {
            entity.HasIndex(e => e.Key).IsUnique();
        });
    }
}

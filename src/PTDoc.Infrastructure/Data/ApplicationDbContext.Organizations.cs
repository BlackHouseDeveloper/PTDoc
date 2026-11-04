using Microsoft.EntityFrameworkCore;
using PTDoc.Core.Models;

namespace PTDoc.Infrastructure.Data;

public partial class ApplicationDbContext
{
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrganizationMembership> OrganizationMemberships => Set<OrganizationMembership>();
    public DbSet<ClinicMembership> ClinicMemberships => Set<ClinicMembership>();
    public DbSet<OrganizationInvitation> OrganizationInvitations => Set<OrganizationInvitation>();

    private static void ConfigureOrganizationModels(ModelBuilder model)
    {
        model.Entity<Organization>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Name).HasMaxLength(200).IsRequired();
            entity.Property(item => item.Version).IsConcurrencyToken();
        });
        model.Entity<Clinic>().HasOne<Organization>().WithMany()
            .HasForeignKey(item => item.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<OrganizationMembership>(entity =>
        {
            entity.HasKey(item => new { item.OrganizationId, item.UserId });
            entity.Property(item => item.Version).IsConcurrencyToken();
            entity.HasOne<Organization>().WithMany().HasForeignKey(item => item.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<User>().WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<ClinicMembership>(entity =>
        {
            entity.HasKey(item => new { item.ClinicId, item.UserId });
            entity.Property(item => item.Role).HasMaxLength(50).IsRequired();
            entity.Property(item => item.Version).IsConcurrencyToken();
            entity.HasOne<Clinic>().WithMany().HasForeignKey(item => item.ClinicId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<User>().WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<OrganizationInvitation>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Email).HasMaxLength(255).IsRequired();
            entity.Property(item => item.TokenHash).HasMaxLength(64).IsRequired();
            entity.HasIndex(item => item.TokenHash).IsUnique();
            entity.Property(item => item.Version).IsConcurrencyToken();
            entity.HasOne<Organization>().WithMany().HasForeignKey(item => item.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<User>().WithMany().HasForeignKey(item => item.InvitedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    // Compatibility for existing clinic creation/registration paths. This runs only on
    // new records or the first assignment of an unassigned legacy account. It never
    // restores a revoked membership or changes clinical ownership.
    private void SeedTrackedOrganizationMemberships()
    {
        var clinics = ChangeTracker.Entries<Clinic>()
            .Where(entry => entry.State == EntityState.Added).Select(entry => entry.Entity).ToArray();
        foreach (var clinic in clinics.Where(clinic => clinic.OrganizationId is null))
        {
            clinic.OrganizationId = clinic.Id;
            Organizations.Add(new Organization { Id = clinic.Id, Name = clinic.Name, CreatedAtUtc = clinic.CreatedAt });
        }
        foreach (var user in ChangeTracker.Entries<User>()
            .Where(entry => entry.Entity.ClinicId.HasValue && (entry.State == EntityState.Added
                || (entry.State == EntityState.Modified && entry.Property(item => item.ClinicId).OriginalValue == null
                    && !ClinicMemberships.Any(member => member.UserId == entry.Entity.Id)
                    && !OrganizationMemberships.Any(member => member.UserId == entry.Entity.Id))))
            .Select(entry => entry.Entity).ToArray())
        {
            var clinic = clinics.FirstOrDefault(item => item.Id == user.ClinicId)
                ?? Clinics.AsNoTracking().SingleOrDefault(item => item.Id == user.ClinicId);
            if (clinic?.OrganizationId is not { } organizationId) continue;
            if (!ClinicMemberships.Local.Any(item => item.ClinicId == clinic.Id && item.UserId == user.Id))
                ClinicMemberships.Add(new ClinicMembership { ClinicId = clinic.Id, UserId = user.Id, Role = user.Role });
            if (!OrganizationMemberships.Local.Any(item => item.OrganizationId == organizationId && item.UserId == user.Id))
            {
                // Only trusted creation of an already-active administrator can bootstrap
                // an ownerless new organization. Public registrations are inactive.
                var bootstrapOwner = user.IsActive && user.Role is ("Admin" or "Owner")
                    && !OrganizationMemberships.Local.Any(item => item.OrganizationId == organizationId && item.Role == OrganizationRole.Owner)
                    && !OrganizationMemberships.Any(item => item.OrganizationId == organizationId && item.Role == OrganizationRole.Owner);
                OrganizationMemberships.Add(new OrganizationMembership
                {
                    OrganizationId = organizationId,
                    UserId = user.Id,
                    Role = bootstrapOwner ? OrganizationRole.Owner : OrganizationRole.Member
                });
            }
        }
    }
}

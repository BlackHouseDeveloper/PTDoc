using Microsoft.EntityFrameworkCore;
using PTDoc.Core.Models;

namespace PTDoc.Infrastructure.Data;

public partial class ApplicationDbContext
{
    public DbSet<IndividualSubscription> IndividualSubscriptions => Set<IndividualSubscription>();
    public DbSet<SubscriptionPayment> SubscriptionPayments => Set<SubscriptionPayment>();

    private void ConfigureSubscriptionModels(ModelBuilder model)
    {
        model.Entity<IndividualSubscription>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.HasIndex(item => item.UserId).IsUnique();
            entity.HasIndex(item => item.OrganizationId).IsUnique();
            entity.HasIndex(item => item.ClinicId).IsUnique();
            entity.HasIndex(item => item.InvoiceNumber).IsUnique().HasFilter(IsNotNullFilter("InvoiceNumber"));
            entity.HasIndex(item => item.RecurringSubscriptionId).IsUnique().HasFilter(IsNotNullFilter("RecurringSubscriptionId"));
            entity.Property(item => item.Amount).HasPrecision(18, 2);
            entity.Property(item => item.Environment).HasMaxLength(16).IsRequired();
            entity.Property(item => item.InvoiceNumber).HasMaxLength(20);
            entity.Property(item => item.InitialTransactionId).HasMaxLength(64);
            entity.Property(item => item.CustomerProfileId).HasMaxLength(64);
            entity.Property(item => item.PaymentProfileId).HasMaxLength(64);
            entity.Property(item => item.RecurringSubscriptionId).HasMaxLength(64);
            entity.Property(item => item.ProviderStatus).HasMaxLength(32).IsRequired();
            entity.Property(item => item.PendingOperation).HasMaxLength(32);
            entity.Property(item => item.Version).IsConcurrencyToken();
            entity.HasOne<User>().WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Organization>().WithMany().HasForeignKey(item => item.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Clinic>().WithMany().HasForeignKey(item => item.ClinicId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<SubscriptionPayment>(entity =>
        {
            entity.HasKey(item => item.TransactionId);
            entity.Property(item => item.TransactionId).HasMaxLength(64);
            entity.HasOne<IndividualSubscription>().WithMany().HasForeignKey(item => item.SubscriptionId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}

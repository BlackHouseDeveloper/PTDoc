using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PTDoc.Infrastructure.Data;

#nullable disable

namespace PTDoc.Infrastructure.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261104010000_AddIndividualSubscriptions")]
public partial class AddIndividualSubscriptions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("IndividualSubscriptions", columns: table => new
        {
            Id = table.Column<Guid>(nullable: false),
            UserId = table.Column<Guid>(nullable: false),
            OrganizationId = table.Column<Guid>(nullable: false),
            ClinicId = table.Column<Guid>(nullable: false),
            Amount = table.Column<decimal>(precision: 18, scale: 2, nullable: false),
            IntervalMonths = table.Column<int>(nullable: false),
            Environment = table.Column<string>(maxLength: 16, nullable: false),
            CreatedAtUtc = table.Column<DateTime>(nullable: false),
            PaidThroughUtc = table.Column<DateTime>(nullable: true),
            InvoiceNumber = table.Column<string>(maxLength: 20, nullable: true),
            InitialTransactionId = table.Column<string>(maxLength: 64, nullable: true),
            CustomerProfileId = table.Column<string>(maxLength: 64, nullable: true),
            PaymentProfileId = table.Column<string>(maxLength: 64, nullable: true),
            RecurringSubscriptionId = table.Column<string>(maxLength: 64, nullable: true),
            RecurringStartsAtUtc = table.Column<DateTime>(nullable: true),
            ProviderStatus = table.Column<string>(maxLength: 32, nullable: false),
            PendingOperation = table.Column<string>(maxLength: 32, nullable: true),
            Version = table.Column<long>(nullable: false),
        }, constraints: table =>
        {
            table.PrimaryKey("PK_IndividualSubscriptions", item => item.Id);
            table.ForeignKey("FK_IndividualSubscriptions_Users_UserId", item => item.UserId, "Users", "Id", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_IndividualSubscriptions_Organizations_OrganizationId", item => item.OrganizationId, "Organizations", "Id", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_IndividualSubscriptions_Clinics_ClinicId", item => item.ClinicId, "Clinics", "Id", onDelete: ReferentialAction.Restrict);
        });
        migrationBuilder.CreateTable("SubscriptionPayments", columns: table => new
        {
            TransactionId = table.Column<string>(maxLength: 64, nullable: false),
            SubscriptionId = table.Column<Guid>(nullable: false),
            PaidAtUtc = table.Column<DateTime>(nullable: false),
            CoveredThroughUtc = table.Column<DateTime>(nullable: false),
        }, constraints: table =>
        {
            table.PrimaryKey("PK_SubscriptionPayments", item => item.TransactionId);
            table.ForeignKey("FK_SubscriptionPayments_IndividualSubscriptions_SubscriptionId", item => item.SubscriptionId, "IndividualSubscriptions", "Id", onDelete: ReferentialAction.Restrict);
        });
        migrationBuilder.CreateIndex("IX_IndividualSubscriptions_UserId", "IndividualSubscriptions", "UserId", unique: true);
        migrationBuilder.CreateIndex("IX_IndividualSubscriptions_OrganizationId", "IndividualSubscriptions", "OrganizationId", unique: true);
        migrationBuilder.CreateIndex("IX_IndividualSubscriptions_ClinicId", "IndividualSubscriptions", "ClinicId", unique: true);
        migrationBuilder.CreateIndex("IX_IndividualSubscriptions_InvoiceNumber", "IndividualSubscriptions", "InvoiceNumber", unique: true, filter: "\"InvoiceNumber\" IS NOT NULL");
        migrationBuilder.CreateIndex("IX_IndividualSubscriptions_RecurringSubscriptionId", "IndividualSubscriptions", "RecurringSubscriptionId", unique: true, filter: "\"RecurringSubscriptionId\" IS NOT NULL");
        migrationBuilder.CreateIndex("IX_SubscriptionPayments_SubscriptionId", "SubscriptionPayments", "SubscriptionId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$ BEGIN IF EXISTS (SELECT 1 FROM "IndividualSubscriptions") THEN RAISE EXCEPTION 'Reconcile subscriptions before rollback; do not remove access controls from subscriber accounts.'; END IF; END $$;
            """);
        migrationBuilder.DropTable("SubscriptionPayments");
        migrationBuilder.DropTable("IndividualSubscriptions");
    }
}

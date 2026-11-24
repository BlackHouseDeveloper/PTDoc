using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTDoc.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class PolicyVisitLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "TotalVisitLimit",
                table: "PatientInsurancePolicies",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VisitLimitType",
                table: "PatientInsurancePolicies",
                type: "TEXT",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Native DROP COLUMN preserves unrelated data and indexes without rebuilding the table.
            migrationBuilder.Sql("""
                ALTER TABLE "PatientInsurancePolicies" DROP COLUMN "TotalVisitLimit";
                ALTER TABLE "PatientInsurancePolicies" DROP COLUMN "VisitLimitType";
                """);
        }
    }
}

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
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VisitLimitType",
                table: "PatientInsurancePolicies",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.DropColumn(
                name: "TotalVisitLimit",
                table: "PatientInsurancePolicies");

            migrationBuilder.DropColumn(
                name: "VisitLimitType",
                table: "PatientInsurancePolicies");

            migrationBuilder.CreateIndex(
                name: "UX_NoteTemplates_ClinicId_NoteType_Variant_Active",
                table: "NoteTemplates",
                columns: new[] { "ClinicId", "NoteType", "Variant" },
                unique: true,
                filter: "[IsArchived] = 0");
        }
    }
}

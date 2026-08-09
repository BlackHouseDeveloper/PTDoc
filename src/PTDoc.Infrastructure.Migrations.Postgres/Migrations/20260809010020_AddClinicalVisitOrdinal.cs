using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PTDoc.Infrastructure.Data;

#nullable disable

namespace PTDoc.Infrastructure.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260809010020_AddClinicalVisitOrdinal")]
public sealed class AddClinicalVisitOrdinal : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        ClinicalVisitOrdinalMigrationOperations.Up(migrationBuilder, "Postgres");

    protected override void Down(MigrationBuilder migrationBuilder) =>
        ClinicalVisitOrdinalMigrationOperations.Down(migrationBuilder);
}

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PTDoc.Infrastructure.Data;
using PTDoc.Infrastructure.Data.Migrations;
#nullable disable
namespace PTDoc.Infrastructure.Data.Migrations;
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260808120020_AddEnterpriseDirectoryInsuranceTemplates")]
public sealed class AddEnterpriseDirectoryInsuranceTemplates : Migration { protected override void Up(MigrationBuilder migrationBuilder) => EnterpriseDataMigrationOperations.Up(migrationBuilder, "\"Npi\" IS NOT NULL AND \"IsArchived\" = FALSE AND \"Status\" = 1", "\"IsArchived\" = FALSE AND \"Status\" = 0", "\"IsArchived\" = FALSE"); protected override void Down(MigrationBuilder migrationBuilder) => EnterpriseDataMigrationOperations.Down(migrationBuilder); }

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PTDoc.Infrastructure.Data;

#nullable disable

namespace PTDoc.Infrastructure.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260714010020_AddProductionIntegrations")]
public sealed class AddProductionIntegrations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => IntegrationMigrationOperations.Up(migrationBuilder);
    protected override void Down(MigrationBuilder migrationBuilder) => IntegrationMigrationOperations.Down(migrationBuilder);
}

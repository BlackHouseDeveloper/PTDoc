using Microsoft.EntityFrameworkCore.Infrastructure;using Microsoft.EntityFrameworkCore.Migrations;using PTDoc.Infrastructure.Data;using PTDoc.Infrastructure.Data.Migrations;
#nullable disable
namespace PTDoc.Infrastructure.Data.Migrations;
[DbContext(typeof(ApplicationDbContext))][Migration("20260808120010_AddEnterpriseDirectoryInsuranceTemplates")]
public sealed class AddEnterpriseDirectoryInsuranceTemplates:Migration{protected override void Up(MigrationBuilder migrationBuilder)=>EnterpriseDataMigrationOperations.Up(migrationBuilder,"[Npi] IS NOT NULL AND [IsArchived] = 0 AND [Status] = 1","[IsArchived] = 0 AND [Status] = 0","[IsArchived] = 0");protected override void Down(MigrationBuilder migrationBuilder)=>EnterpriseDataMigrationOperations.Down(migrationBuilder);}

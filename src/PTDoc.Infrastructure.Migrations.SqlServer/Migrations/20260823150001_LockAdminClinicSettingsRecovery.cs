using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTDoc.Infrastructure.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260823150001_LockAdminClinicSettingsRecovery")]
public sealed class LockAdminClinicSettingsRecovery : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE [RoleCapabilityPermissions]
            SET [Level] = 3, [LockedMinimum] = 3
            WHERE [RoleKey] = 'Admin' AND [CapabilityKey] = 28;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE [RoleCapabilityPermissions]
            SET [LockedMinimum] = 0
            WHERE [RoleKey] = 'Admin' AND [CapabilityKey] = 28;
            """);
    }
}

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTDoc.Infrastructure.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260823150002_LockAdminClinicSettingsRecovery")]
public sealed class LockAdminClinicSettingsRecovery : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE "LockAdminClinicSettingsRecoveryBackup" (
                "ClinicId" uuid NOT NULL PRIMARY KEY,
                "Level" integer NOT NULL,
                "LockedMinimum" integer NOT NULL
            );

            INSERT INTO "LockAdminClinicSettingsRecoveryBackup" ("ClinicId", "Level", "LockedMinimum")
            SELECT "ClinicId", "Level", "LockedMinimum"
            FROM "RoleCapabilityPermissions"
            WHERE "RoleKey" = 'Admin' AND "CapabilityKey" = 28;

            UPDATE "RoleCapabilityPermissions"
            SET "Level" = 3, "LockedMinimum" = 3
            WHERE "RoleKey" = 'Admin' AND "CapabilityKey" = 28;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE "RoleCapabilityPermissions" permission
            SET "Level" = backup."Level",
                "LockedMinimum" = backup."LockedMinimum"
            FROM "LockAdminClinicSettingsRecoveryBackup" backup
            WHERE backup."ClinicId" = permission."ClinicId"
              AND permission."RoleKey" = 'Admin'
              AND permission."CapabilityKey" = 28;

            DROP TABLE "LockAdminClinicSettingsRecoveryBackup";
            """);
    }
}

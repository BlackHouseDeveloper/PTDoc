using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTDoc.Infrastructure.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260823150000_LockAdminClinicSettingsRecovery")]
public sealed class LockAdminClinicSettingsRecovery : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE "LockAdminClinicSettingsRecoveryBackup" (
                "ClinicId" TEXT NOT NULL PRIMARY KEY,
                "Level" INTEGER NOT NULL,
                "LockedMinimum" INTEGER NOT NULL
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
            UPDATE "RoleCapabilityPermissions"
            SET "Level" = (
                    SELECT backup."Level"
                    FROM "LockAdminClinicSettingsRecoveryBackup" backup
                    WHERE backup."ClinicId" = "RoleCapabilityPermissions"."ClinicId"),
                "LockedMinimum" = (
                    SELECT backup."LockedMinimum"
                    FROM "LockAdminClinicSettingsRecoveryBackup" backup
                    WHERE backup."ClinicId" = "RoleCapabilityPermissions"."ClinicId")
            WHERE "RoleKey" = 'Admin'
              AND "CapabilityKey" = 28
              AND EXISTS (
                  SELECT 1
                  FROM "LockAdminClinicSettingsRecoveryBackup" backup
                  WHERE backup."ClinicId" = "RoleCapabilityPermissions"."ClinicId");

            UPDATE "RoleCapabilityPermissions"
            SET "Level" = 3, "LockedMinimum" = 0
            WHERE "RoleKey" = 'Admin'
              AND "CapabilityKey" = 28
              AND NOT EXISTS (
                  SELECT 1
                  FROM "LockAdminClinicSettingsRecoveryBackup" backup
                  WHERE backup."ClinicId" = "RoleCapabilityPermissions"."ClinicId");

            DROP TABLE "LockAdminClinicSettingsRecoveryBackup";
            """);
    }
}

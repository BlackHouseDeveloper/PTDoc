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
            CREATE TABLE [LockAdminClinicSettingsRecoveryBackup] (
                [ClinicId] uniqueidentifier NOT NULL PRIMARY KEY,
                [Level] int NOT NULL,
                [LockedMinimum] int NOT NULL
            );

            INSERT INTO [LockAdminClinicSettingsRecoveryBackup] ([ClinicId], [Level], [LockedMinimum])
            SELECT [ClinicId], [Level], [LockedMinimum]
            FROM [RoleCapabilityPermissions]
            WHERE [RoleKey] = 'Admin' AND [CapabilityKey] = 28;

            UPDATE [RoleCapabilityPermissions]
            SET [Level] = 3, [LockedMinimum] = 3
            WHERE [RoleKey] = 'Admin' AND [CapabilityKey] = 28;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE permission
            SET [Level] = recovery_backup.[Level],
                [LockedMinimum] = recovery_backup.[LockedMinimum]
            FROM [RoleCapabilityPermissions] permission
            INNER JOIN [LockAdminClinicSettingsRecoveryBackup] recovery_backup
                ON recovery_backup.[ClinicId] = permission.[ClinicId]
            WHERE permission.[RoleKey] = 'Admin' AND permission.[CapabilityKey] = 28;

            DROP TABLE [LockAdminClinicSettingsRecoveryBackup];
            """);
    }
}

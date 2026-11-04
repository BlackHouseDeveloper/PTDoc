using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTDoc.Infrastructure.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261103030000_AddOrganizationMemberships")]
public partial class AddOrganizationMemberships : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE "Organizations" (
                "Id" TEXT NOT NULL,
                "Name" TEXT NOT NULL,
                "IsActive" INTEGER NOT NULL,
                "Version" INTEGER NOT NULL,
                "CreatedAtUtc" TEXT NOT NULL,
                CONSTRAINT "PK_Organizations" PRIMARY KEY ("Id")
            );
            ALTER TABLE "Clinics" ADD "OrganizationId" TEXT NULL REFERENCES "Organizations" ("Id") ON DELETE RESTRICT;
            CREATE INDEX "IX_Clinics_OrganizationId" ON "Clinics" ("OrganizationId");
            ALTER TABLE "Sessions" ADD "ClinicId" TEXT NULL;
            INSERT INTO "Organizations" ("Id", "Name", "IsActive", "Version", "CreatedAtUtc") SELECT "Id", "Name", "IsActive", 1, "CreatedAt" FROM "Clinics";
            UPDATE "Clinics" SET "OrganizationId" = "Id";
            CREATE TABLE "OrganizationMemberships" (
                "OrganizationId" TEXT NOT NULL,
                "UserId" TEXT NOT NULL,
                "Role" INTEGER NOT NULL,
                "IsActive" INTEGER NOT NULL,
                "Version" INTEGER NOT NULL,
                CONSTRAINT "PK_OrganizationMemberships" PRIMARY KEY ("OrganizationId", "UserId"),
                CONSTRAINT "FK_OrganizationMemberships_Organizations_OrganizationId" FOREIGN KEY ("OrganizationId") REFERENCES "Organizations" ("Id"),
                CONSTRAINT "FK_OrganizationMemberships_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id")
            );
            CREATE INDEX "IX_OrganizationMemberships_UserId" ON "OrganizationMemberships" ("UserId");
            CREATE TABLE "ClinicMemberships" (
                "ClinicId" TEXT NOT NULL,
                "UserId" TEXT NOT NULL,
                "Role" TEXT NOT NULL,
                "IsActive" INTEGER NOT NULL,
                "Version" INTEGER NOT NULL,
                CONSTRAINT "PK_ClinicMemberships" PRIMARY KEY ("ClinicId", "UserId"),
                CONSTRAINT "FK_ClinicMemberships_Clinics_ClinicId" FOREIGN KEY ("ClinicId") REFERENCES "Clinics" ("Id"),
                CONSTRAINT "FK_ClinicMemberships_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id")
            );
            CREATE INDEX "IX_ClinicMemberships_UserId" ON "ClinicMemberships" ("UserId");
            CREATE TABLE "OrganizationInvitations" (
                "Id" TEXT NOT NULL,
                "OrganizationId" TEXT NOT NULL,
                "Email" TEXT NOT NULL,
                "TokenHash" TEXT NOT NULL,
                "ExpiresAtUtc" TEXT NOT NULL,
                "AcceptedAtUtc" TEXT NULL,
                "InvitedByUserId" TEXT NOT NULL,
                "Version" INTEGER NOT NULL,
                CONSTRAINT "PK_OrganizationInvitations" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_OrganizationInvitations_Organizations_OrganizationId" FOREIGN KEY ("OrganizationId") REFERENCES "Organizations" ("Id"),
                CONSTRAINT "FK_OrganizationInvitations_Users_InvitedByUserId" FOREIGN KEY ("InvitedByUserId") REFERENCES "Users" ("Id")
            );
            CREATE INDEX "IX_OrganizationInvitations_OrganizationId" ON "OrganizationInvitations" ("OrganizationId");
            CREATE INDEX "IX_OrganizationInvitations_InvitedByUserId" ON "OrganizationInvitations" ("InvitedByUserId");
            CREATE UNIQUE INDEX "IX_OrganizationInvitations_TokenHash" ON "OrganizationInvitations" ("TokenHash");
            INSERT INTO "OrganizationMemberships" ("OrganizationId", "UserId", "Role", "IsActive", "Version")
            SELECT c."OrganizationId", u."Id", CASE WHEN u."IsActive" = 1 AND u."Role" IN ('Admin','Owner') THEN 2 ELSE 0 END, 1, 1
            FROM "Users" u JOIN "Clinics" c ON c."Id" = u."ClinicId";
            INSERT INTO "ClinicMemberships" ("ClinicId", "UserId", "Role", "IsActive", "Version")
            SELECT "ClinicId", "Id", "Role", 1, 1 FROM "Users" WHERE "ClinicId" IS NOT NULL;
            CREATE TABLE "ScheduleBlockRules_Plan05" (
                "Id" TEXT NOT NULL,
                "ClinicId" TEXT NOT NULL,
                "ClinicianId" TEXT NULL,
                "Name" TEXT NOT NULL,
                "ReasonCode" TEXT NOT NULL,
                "Weekdays" INTEGER NOT NULL,
                "StartLocalTime" TEXT NOT NULL,
                "EndLocalTime" TEXT NOT NULL,
                "EffectiveStartDate" TEXT NOT NULL,
                "EffectiveEndDate" TEXT NULL,
                "IsRecurring" INTEGER NOT NULL,
                "IsActive" INTEGER NOT NULL,
                "Version" INTEGER NOT NULL,
                "UpdatedByUserId" TEXT NOT NULL,
                "CreatedAtUtc" TEXT NOT NULL,
                "UpdatedAtUtc" TEXT NOT NULL,
                PRIMARY KEY ("Id"),
                FOREIGN KEY ("ClinicId") REFERENCES "Clinics" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_ScheduleBlockRules_ClinicMemberships_ClinicId_ClinicianId" FOREIGN KEY ("ClinicId", "ClinicianId") REFERENCES "ClinicMemberships" ("ClinicId", "UserId") ON DELETE RESTRICT
            );
            INSERT INTO "ScheduleBlockRules_Plan05" ("Id", "ClinicId", "ClinicianId", "Name", "ReasonCode", "Weekdays", "StartLocalTime", "EndLocalTime", "EffectiveStartDate", "EffectiveEndDate", "IsRecurring", "IsActive", "Version", "UpdatedByUserId", "CreatedAtUtc", "UpdatedAtUtc") SELECT "Id", "ClinicId", "ClinicianId", "Name", "ReasonCode", "Weekdays", "StartLocalTime", "EndLocalTime", "EffectiveStartDate", "EffectiveEndDate", "IsRecurring", "IsActive", "Version", "UpdatedByUserId", "CreatedAtUtc", "UpdatedAtUtc" FROM "ScheduleBlockRules";
            DROP TABLE "ScheduleBlockRules";
            ALTER TABLE "ScheduleBlockRules_Plan05" RENAME TO "ScheduleBlockRules";
            CREATE INDEX "IX_ScheduleBlockRules_ClinicId_ClinicianId" ON "ScheduleBlockRules" ("ClinicId", "ClinicianId");
            CREATE INDEX "IX_ScheduleBlockRules_ClinicId_IsActive" ON "ScheduleBlockRules" ("ClinicId", "IsActive");
            CREATE INDEX "IX_ScheduleBlockRules_ClinicianId" ON "ScheduleBlockRules" ("ClinicianId");
            """);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TEMP TABLE "Plan05RollbackGuard" ("Safe" INTEGER CHECK ("Safe" = 1));
            INSERT INTO "Plan05RollbackGuard" SELECT CASE WHEN EXISTS (SELECT 1 FROM "ClinicMemberships" m JOIN "Users" u ON u."Id" = m."UserId"
            WHERE u."ClinicId" IS NULL OR m."ClinicId" <> u."ClinicId" OR m."Role" <> u."Role" OR m."IsActive" <> 1)
            OR EXISTS (SELECT 1 FROM "OrganizationMemberships" WHERE "IsActive" <> 1)
            OR EXISTS (SELECT 1 FROM "Organizations" WHERE "IsActive" <> 1)
            OR EXISTS (SELECT 1 FROM "Clinics" WHERE "OrganizationId" <> "Id")
            OR EXISTS (SELECT 1 FROM "OrganizationMemberships" m JOIN "Users" u ON u."Id" = m."UserId"
                WHERE u."ClinicId" IS NULL OR m."OrganizationId" <> u."ClinicId"
                    OR m."Role" <> CASE WHEN u."IsActive" = 1 AND u."Role" IN ('Admin','Owner') THEN 2 ELSE 0 END)
            OR EXISTS (SELECT 1 FROM "Sessions" s JOIN "Users" u ON u."Id" = s."UserId"
                WHERE s."ClinicId" IS NOT NULL AND (u."ClinicId" IS NULL OR s."ClinicId" <> u."ClinicId"))
            OR EXISTS (SELECT 1 FROM "OrganizationInvitations") THEN 0 ELSE 1 END;
            DROP TABLE "Plan05RollbackGuard";
            CREATE TABLE "ScheduleBlockRules_Plan05" (
                "Id" TEXT NOT NULL,
                "ClinicId" TEXT NOT NULL,
                "ClinicianId" TEXT NULL,
                "Name" TEXT NOT NULL,
                "ReasonCode" TEXT NOT NULL,
                "Weekdays" INTEGER NOT NULL,
                "StartLocalTime" TEXT NOT NULL,
                "EndLocalTime" TEXT NOT NULL,
                "EffectiveStartDate" TEXT NOT NULL,
                "EffectiveEndDate" TEXT NULL,
                "IsRecurring" INTEGER NOT NULL,
                "IsActive" INTEGER NOT NULL,
                "Version" INTEGER NOT NULL,
                "UpdatedByUserId" TEXT NOT NULL,
                "CreatedAtUtc" TEXT NOT NULL,
                "UpdatedAtUtc" TEXT NOT NULL,
                PRIMARY KEY ("Id"),
                FOREIGN KEY ("ClinicId") REFERENCES "Clinics" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_ScheduleBlockRules_Users_ClinicId_ClinicianId" FOREIGN KEY ("ClinicId", "ClinicianId") REFERENCES "Users" ("ClinicId", "Id") ON DELETE RESTRICT
            );
            INSERT INTO "ScheduleBlockRules_Plan05" ("Id", "ClinicId", "ClinicianId", "Name", "ReasonCode", "Weekdays", "StartLocalTime", "EndLocalTime", "EffectiveStartDate", "EffectiveEndDate", "IsRecurring", "IsActive", "Version", "UpdatedByUserId", "CreatedAtUtc", "UpdatedAtUtc") SELECT "Id", "ClinicId", "ClinicianId", "Name", "ReasonCode", "Weekdays", "StartLocalTime", "EndLocalTime", "EffectiveStartDate", "EffectiveEndDate", "IsRecurring", "IsActive", "Version", "UpdatedByUserId", "CreatedAtUtc", "UpdatedAtUtc" FROM "ScheduleBlockRules";
            DROP TABLE "ScheduleBlockRules";
            ALTER TABLE "ScheduleBlockRules_Plan05" RENAME TO "ScheduleBlockRules";
            CREATE INDEX "IX_ScheduleBlockRules_ClinicId_ClinicianId" ON "ScheduleBlockRules" ("ClinicId", "ClinicianId");
            CREATE INDEX "IX_ScheduleBlockRules_ClinicId_IsActive" ON "ScheduleBlockRules" ("ClinicId", "IsActive");
            CREATE INDEX "IX_ScheduleBlockRules_ClinicianId" ON "ScheduleBlockRules" ("ClinicianId");
            DROP TABLE "OrganizationInvitations";
            DROP TABLE "ClinicMemberships";
            DROP TABLE "OrganizationMemberships";
            DROP INDEX "IX_Clinics_OrganizationId";
            ALTER TABLE "Clinics" DROP COLUMN "OrganizationId";
            ALTER TABLE "Sessions" DROP COLUMN "ClinicId";
            DROP TABLE "Organizations";
            """);
    }
}

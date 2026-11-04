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
                "Id" uuid NOT NULL,
                "Name" character varying(200) NOT NULL,
                "IsActive" boolean NOT NULL,
                "Version" bigint NOT NULL,
                "CreatedAtUtc" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_Organizations" PRIMARY KEY ("Id")
            );
            ALTER TABLE "Clinics" ADD "OrganizationId" uuid NULL;
            ALTER TABLE "Clinics" ADD CONSTRAINT "FK_Clinics_Organizations_OrganizationId" FOREIGN KEY ("OrganizationId") REFERENCES "Organizations" ("Id");
            CREATE INDEX "IX_Clinics_OrganizationId" ON "Clinics" ("OrganizationId");
            ALTER TABLE "Sessions" ADD "ClinicId" uuid NULL;
            INSERT INTO "Organizations" ("Id", "Name", "IsActive", "Version", "CreatedAtUtc") SELECT "Id", "Name", "IsActive", 1, "CreatedAt" FROM "Clinics";
            UPDATE "Clinics" SET "OrganizationId" = "Id";
            CREATE TABLE "OrganizationMemberships" (
                "OrganizationId" uuid NOT NULL,
                "UserId" uuid NOT NULL,
                "Role" integer NOT NULL,
                "IsActive" boolean NOT NULL,
                "Version" bigint NOT NULL,
                CONSTRAINT "PK_OrganizationMemberships" PRIMARY KEY ("OrganizationId", "UserId"),
                CONSTRAINT "FK_OrganizationMemberships_Organizations_OrganizationId" FOREIGN KEY ("OrganizationId") REFERENCES "Organizations" ("Id"),
                CONSTRAINT "FK_OrganizationMemberships_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id")
            );
            CREATE INDEX "IX_OrganizationMemberships_UserId" ON "OrganizationMemberships" ("UserId");
            CREATE TABLE "ClinicMemberships" (
                "ClinicId" uuid NOT NULL,
                "UserId" uuid NOT NULL,
                "Role" character varying(50) NOT NULL,
                "IsActive" boolean NOT NULL,
                "Version" bigint NOT NULL,
                CONSTRAINT "PK_ClinicMemberships" PRIMARY KEY ("ClinicId", "UserId"),
                CONSTRAINT "FK_ClinicMemberships_Clinics_ClinicId" FOREIGN KEY ("ClinicId") REFERENCES "Clinics" ("Id"),
                CONSTRAINT "FK_ClinicMemberships_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id")
            );
            CREATE INDEX "IX_ClinicMemberships_UserId" ON "ClinicMemberships" ("UserId");
            CREATE TABLE "OrganizationInvitations" (
                "Id" uuid NOT NULL,
                "OrganizationId" uuid NOT NULL,
                "Email" character varying(255) NOT NULL,
                "TokenHash" character varying(64) NOT NULL,
                "ExpiresAtUtc" timestamp with time zone NOT NULL,
                "AcceptedAtUtc" timestamp with time zone NULL,
                "InvitedByUserId" uuid NOT NULL,
                "Version" bigint NOT NULL,
                CONSTRAINT "PK_OrganizationInvitations" PRIMARY KEY ("Id"),
                CONSTRAINT "FK_OrganizationInvitations_Organizations_OrganizationId" FOREIGN KEY ("OrganizationId") REFERENCES "Organizations" ("Id"),
                CONSTRAINT "FK_OrganizationInvitations_Users_InvitedByUserId" FOREIGN KEY ("InvitedByUserId") REFERENCES "Users" ("Id")
            );
            CREATE INDEX "IX_OrganizationInvitations_OrganizationId" ON "OrganizationInvitations" ("OrganizationId");
            CREATE INDEX "IX_OrganizationInvitations_InvitedByUserId" ON "OrganizationInvitations" ("InvitedByUserId");
            CREATE UNIQUE INDEX "IX_OrganizationInvitations_TokenHash" ON "OrganizationInvitations" ("TokenHash");
            INSERT INTO "OrganizationMemberships" ("OrganizationId", "UserId", "Role", "IsActive", "Version")
            SELECT c."OrganizationId", u."Id", CASE WHEN u."IsActive" = TRUE AND u."Role" IN ('Admin','Owner') THEN 2 ELSE 0 END, TRUE, 1
            FROM "Users" u JOIN "Clinics" c ON c."Id" = u."ClinicId";
            INSERT INTO "ClinicMemberships" ("ClinicId", "UserId", "Role", "IsActive", "Version")
            SELECT "ClinicId", "Id", "Role", TRUE, 1 FROM "Users" WHERE "ClinicId" IS NOT NULL;
            ALTER TABLE "ScheduleBlockRules" DROP CONSTRAINT "FK_ScheduleBlockRules_Users_ClinicId_ClinicianId";
            ALTER TABLE "ScheduleBlockRules" ADD CONSTRAINT "FK_ScheduleBlockRules_ClinicMemberships_ClinicId_ClinicianId" FOREIGN KEY ("ClinicId", "ClinicianId") REFERENCES "ClinicMemberships" ("ClinicId", "UserId");
            """);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$ BEGIN IF EXISTS (SELECT 1 FROM "ClinicMemberships" m JOIN "Users" u ON u."Id" = m."UserId"
            WHERE u."ClinicId" IS NULL OR m."ClinicId" <> u."ClinicId" OR m."Role" <> u."Role" OR m."IsActive" <> TRUE)
            OR EXISTS (SELECT 1 FROM "OrganizationMemberships" WHERE "IsActive" <> TRUE)
            OR EXISTS (SELECT 1 FROM "Organizations" WHERE "IsActive" <> TRUE)
            OR EXISTS (SELECT 1 FROM "Clinics" WHERE "OrganizationId" <> "Id")
            OR EXISTS (SELECT 1 FROM "OrganizationMemberships" m JOIN "Users" u ON u."Id" = m."UserId"
                WHERE u."ClinicId" IS NULL OR m."OrganizationId" <> u."ClinicId"
                    OR m."Role" <> CASE WHEN u."IsActive" = TRUE AND u."Role" IN ('Admin','Owner') THEN 2 ELSE 0 END)
            OR EXISTS (SELECT 1 FROM "Sessions" s JOIN "Users" u ON u."Id" = s."UserId"
                WHERE s."ClinicId" IS NOT NULL AND (u."ClinicId" IS NULL OR s."ClinicId" <> u."ClinicId"))
            OR EXISTS (SELECT 1 FROM "OrganizationInvitations") THEN RAISE EXCEPTION 'Reconcile organization memberships before rollback.'; END IF; END $$;
            ALTER TABLE "ScheduleBlockRules" DROP CONSTRAINT "FK_ScheduleBlockRules_ClinicMemberships_ClinicId_ClinicianId";
            ALTER TABLE "ScheduleBlockRules" ADD CONSTRAINT "FK_ScheduleBlockRules_Users_ClinicId_ClinicianId" FOREIGN KEY ("ClinicId", "ClinicianId") REFERENCES "Users" ("ClinicId", "Id");
            DROP TABLE "OrganizationInvitations";
            DROP TABLE "ClinicMemberships";
            DROP TABLE "OrganizationMemberships";
            ALTER TABLE "Clinics" DROP CONSTRAINT "FK_Clinics_Organizations_OrganizationId";
            DROP INDEX "IX_Clinics_OrganizationId";
            ALTER TABLE "Clinics" DROP COLUMN "OrganizationId";
            ALTER TABLE "Sessions" DROP COLUMN "ClinicId";
            DROP TABLE "Organizations";
            """);
    }
}

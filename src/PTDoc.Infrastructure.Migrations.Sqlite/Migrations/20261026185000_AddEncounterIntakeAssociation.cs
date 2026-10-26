using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTDoc.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEncounterIntakeAssociation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EncounterIsReEvaluation",
                table: "IntakeForms",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "EncounterNoteId",
                table: "IntakeForms",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QuestionnaireDefinitionId",
                table: "IntakeForms",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntakeForms_EncounterNoteId",
                table: "IntakeForms",
                column: "EncounterNoteId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_IntakeForms_ClinicalNotes_EncounterNoteId",
                table: "IntakeForms",
                column: "EncounterNoteId",
                principalTable: "ClinicalNotes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The preceding hand-authored migration has no target model for EF's
            // automatic SQLite rebuild. Preserve legacy columns, indexes and dependent
            // delivery records explicitly. Export encounter associations before downgrade.
            migrationBuilder.Sql("PRAGMA foreign_keys = OFF;", suppressTransaction: true);
            migrationBuilder.Sql("""
                CREATE TABLE "__legacy_IntakeForms" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_IntakeForms" PRIMARY KEY,
                    "AccessToken" TEXT NOT NULL,
                    "ClinicId" TEXT NULL,
                    "ExpiresAt" TEXT NULL,
                    "IsLocked" INTEGER NOT NULL,
                    "LastModifiedUtc" TEXT NOT NULL,
                    "ModifiedByUserId" TEXT NOT NULL,
                    "PatientId" TEXT NOT NULL,
                    "ResponseJson" TEXT NOT NULL,
                    "SubmittedAt" TEXT NULL,
                    "SyncState" INTEGER NOT NULL,
                    "TemplateVersion" TEXT NOT NULL, "Consents" TEXT NOT NULL DEFAULT '{}', "PainMapData" TEXT NOT NULL DEFAULT '{}', "StructuredDataJson" TEXT NULL, "InviteToken" TEXT NULL, "ReviewedAtUtc" TEXT NULL, "ReviewedByUserId" TEXT NULL,
                    CONSTRAINT "FK_IntakeForms_Clinics_ClinicId" FOREIGN KEY ("ClinicId") REFERENCES "Clinics" ("Id") ON DELETE RESTRICT,
                    CONSTRAINT "FK_IntakeForms_Patients_PatientId" FOREIGN KEY ("PatientId") REFERENCES "Patients" ("Id") ON DELETE RESTRICT
                );
                INSERT INTO "__legacy_IntakeForms" ("Id", "AccessToken", "ClinicId", "ExpiresAt", "IsLocked", "LastModifiedUtc", "ModifiedByUserId", "PatientId", "ResponseJson", "SubmittedAt", "SyncState", "TemplateVersion", "Consents", "PainMapData", "StructuredDataJson", "InviteToken", "ReviewedAtUtc", "ReviewedByUserId") SELECT "Id", "AccessToken", "ClinicId", "ExpiresAt", "IsLocked", "LastModifiedUtc", "ModifiedByUserId", "PatientId", "ResponseJson", "SubmittedAt", "SyncState", "TemplateVersion", "Consents", "PainMapData", "StructuredDataJson", "InviteToken", "ReviewedAtUtc", "ReviewedByUserId" FROM "IntakeForms";
                DROP TABLE "IntakeForms";
                ALTER TABLE "__legacy_IntakeForms" RENAME TO "IntakeForms";
                CREATE UNIQUE INDEX "IX_IntakeForms_AccessToken" ON "IntakeForms" ("AccessToken");
                CREATE INDEX "IX_IntakeForms_ClinicId" ON "IntakeForms" ("ClinicId") WHERE ClinicId IS NOT NULL;
                CREATE INDEX "IX_IntakeForms_LastModifiedUtc" ON "IntakeForms" ("LastModifiedUtc");
                CREATE INDEX "IX_IntakeForms_PatientId" ON "IntakeForms" ("PatientId");
                CREATE INDEX "IX_IntakeForms_ReviewedAtUtc" ON "IntakeForms" ("ReviewedAtUtc");
                """);
            migrationBuilder.Sql("PRAGMA foreign_keys = ON;", suppressTransaction: true);
        }
    }
}

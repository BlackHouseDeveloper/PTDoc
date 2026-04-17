using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PTDoc.Infrastructure.Data;

#nullable disable

namespace PTDoc.Infrastructure.Data.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260417160000_RetireLegacyAddendumStore")]
    public partial class RetireLegacyAddendumStore : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO "ClinicalNotes" (
                    "Id",
                    "CreatedUtc",
                    "LastModifiedUtc",
                    "ModifiedByUserId",
                    "SyncState",
                    "PatientId",
                    "AppointmentId",
                    "ParentNoteId",
                    "IsAddendum",
                    "NoteType",
                    "IsReEvaluation",
                    "NoteStatus",
                    "TherapistNpi",
                    "TotalTreatmentMinutes",
                    "PhysicianSignatureHash",
                    "PhysicianSignedUtc",
                    "PhysicianSignedByUserId",
                    "ContentJson",
                    "DateOfService",
                    "SignatureHash",
                    "SignedUtc",
                    "SignedByUserId",
                    "RequiresCoSign",
                    "CoSignedByUserId",
                    "CoSignedUtc",
                    "CptCodesJson",
                    "ClinicId"
                )
                SELECT
                    a."Id",
                    a."CreatedUtc",
                    a."LastModifiedUtc",
                    a."ModifiedByUserId",
                    a."SyncState",
                    parent."PatientId",
                    parent."AppointmentId",
                    a."ClinicalNoteId",
                    TRUE,
                    parent."NoteType",
                    parent."IsReEvaluation",
                    CASE
                        WHEN a."SignatureHash" IS NOT NULL AND a."SignatureHash" <> '' THEN 2
                        ELSE 0
                    END,
                    parent."TherapistNpi",
                    NULL,
                    NULL,
                    NULL,
                    NULL,
                    to_json(COALESCE(a."Content", ''))::text,
                    parent."DateOfService",
                    a."SignatureHash",
                    NULL,
                    NULL,
                    FALSE,
                    NULL,
                    NULL,
                    '[]',
                    parent."ClinicId"
                FROM "Addendums" AS a
                INNER JOIN "ClinicalNotes" AS parent ON parent."Id" = a."ClinicalNoteId"
                LEFT JOIN "ClinicalNotes" AS existing ON existing."Id" = a."Id"
                WHERE existing."Id" IS NULL;
                """);

            migrationBuilder.DropTable(
                name: "Addendums");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Addendums",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LastModifiedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SyncState = table.Column<int>(type: "integer", nullable: false),
                    ClinicalNoteId = table.Column<Guid>(type: "uuid", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SignatureHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Addendums", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Addendums_ClinicalNotes_ClinicalNoteId",
                        column: x => x.ClinicalNoteId,
                        principalTable: "ClinicalNotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Addendums_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Addendums_ClinicalNoteId",
                table: "Addendums",
                column: "ClinicalNoteId");

            migrationBuilder.CreateIndex(
                name: "IX_Addendums_CreatedByUserId",
                table: "Addendums",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Addendums_CreatedUtc",
                table: "Addendums",
                column: "CreatedUtc");

            migrationBuilder.Sql("""
                INSERT INTO "Addendums" (
                    "Id",
                    "LastModifiedUtc",
                    "ModifiedByUserId",
                    "SyncState",
                    "ClinicalNoteId",
                    "Content",
                    "CreatedUtc",
                    "CreatedByUserId",
                    "SignatureHash"
                )
                SELECT
                    note."Id",
                    note."LastModifiedUtc",
                    note."ModifiedByUserId",
                    note."SyncState",
                    note."ParentNoteId",
                    CASE
                        WHEN jsonb_typeof(note."ContentJson"::jsonb) = 'string'
                            THEN note."ContentJson"::jsonb #>> '{}'
                        ELSE note."ContentJson"
                    END,
                    note."CreatedUtc",
                    note."ModifiedByUserId",
                    note."SignatureHash"
                FROM "ClinicalNotes" AS note
                WHERE note."IsAddendum" = TRUE
                  AND note."ParentNoteId" IS NOT NULL;
                """);

            migrationBuilder.Sql("""
                DELETE FROM "ClinicalNotes"
                WHERE "IsAddendum" = TRUE;
                """);
        }
    }
}

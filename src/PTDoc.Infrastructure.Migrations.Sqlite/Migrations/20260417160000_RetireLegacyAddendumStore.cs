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
                    1,
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
                    json_quote(COALESCE(a."Content", '')),
                    parent."DateOfService",
                    a."SignatureHash",
                    NULL,
                    NULL,
                    0,
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
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LastModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SyncState = table.Column<int>(type: "INTEGER", nullable: false),
                    ClinicalNoteId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SignatureHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
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
                        WHEN json_valid(note."ContentJson") AND json_type(note."ContentJson") = 'text'
                            THEN json_extract(note."ContentJson", '$')
                        ELSE note."ContentJson"
                    END,
                    note."CreatedUtc",
                    note."ModifiedByUserId",
                    note."SignatureHash"
                FROM "ClinicalNotes" AS note
                WHERE note."IsAddendum" = 1
                  AND note."ParentNoteId" IS NOT NULL;
                """);

            migrationBuilder.Sql("""
                DELETE FROM "ClinicalNotes"
                WHERE "IsAddendum" = 1;
                """);
        }
    }
}

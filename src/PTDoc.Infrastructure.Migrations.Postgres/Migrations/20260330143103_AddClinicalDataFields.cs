using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTDoc.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddClinicalDataFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AuthorizationNumber",
                table: "Patients",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ConsentSigned",
                table: "Patients",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ConsentSignedDate",
                table: "Patients",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DateOfOnset",
                table: "Patients",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DiagnosisCodesJson",
                table: "Patients",
                type: "text",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "EmergencyContactName",
                table: "Patients",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmergencyContactPhone",
                table: "Patients",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhysicianNpi",
                table: "Patients",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferringPhysician",
                table: "Patients",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RetentionYears",
                table: "Patients",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastModifiedUtc",
                table: "ObjectiveMetrics",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "Side",
                table: "ObjectiveMetrics",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Unit",
                table: "ObjectiveMetrics",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsReEvaluation",
                table: "ClinicalNotes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "NoteStatus",
                table: "ClinicalNotes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PhysicianSignatureHash",
                table: "ClinicalNotes",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PhysicianSignedByUserId",
                table: "ClinicalNotes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PhysicianSignedUtc",
                table: "ClinicalNotes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TherapistNpi",
                table: "ClinicalNotes",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TotalTreatmentMinutes",
                table: "ClinicalNotes",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "NoteTaxonomySelections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicalNoteId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CategoryTitle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CategoryKind = table.Column<int>(type: "integer", nullable: false),
                    ItemId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ItemLabel = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NoteTaxonomySelections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NoteTaxonomySelections_ClinicalNotes_ClinicalNoteId",
                        column: x => x.ClinicalNoteId,
                        principalTable: "ClinicalNotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PatientGoals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginatingNoteId = table.Column<Guid>(type: "uuid", nullable: true),
                    MetByNoteId = table.Column<Guid>(type: "uuid", nullable: true),
                    ArchivedByNoteId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: true),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Category = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Timeframe = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    MatchedFunctionalLimitationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CompletionReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    MetUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ArchivedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatientGoals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PatientGoals_ClinicalNotes_ArchivedByNoteId",
                        column: x => x.ArchivedByNoteId,
                        principalTable: "ClinicalNotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PatientGoals_ClinicalNotes_MetByNoteId",
                        column: x => x.MetByNoteId,
                        principalTable: "ClinicalNotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PatientGoals_ClinicalNotes_OriginatingNoteId",
                        column: x => x.OriginatingNoteId,
                        principalTable: "ClinicalNotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PatientGoals_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PatientGoals_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NoteTaxonomySelections_CategoryId",
                table: "NoteTaxonomySelections",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_NoteTaxonomySelections_CategoryId_ItemId",
                table: "NoteTaxonomySelections",
                columns: new[] { "CategoryId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_NoteTaxonomySelections_ClinicalNoteId",
                table: "NoteTaxonomySelections",
                column: "ClinicalNoteId");

            migrationBuilder.CreateIndex(
                name: "IX_NoteTaxonomySelections_ItemId",
                table: "NoteTaxonomySelections",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PatientGoals_ArchivedByNoteId",
                table: "PatientGoals",
                column: "ArchivedByNoteId");

            migrationBuilder.CreateIndex(
                name: "IX_PatientGoals_ClinicId",
                table: "PatientGoals",
                column: "ClinicId",
                filter: "ClinicId IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PatientGoals_MetByNoteId",
                table: "PatientGoals",
                column: "MetByNoteId");

            migrationBuilder.CreateIndex(
                name: "IX_PatientGoals_OriginatingNoteId",
                table: "PatientGoals",
                column: "OriginatingNoteId");

            migrationBuilder.CreateIndex(
                name: "IX_PatientGoals_PatientId",
                table: "PatientGoals",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_PatientGoals_PatientId_Status",
                table: "PatientGoals",
                columns: new[] { "PatientId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NoteTaxonomySelections");

            migrationBuilder.DropTable(
                name: "PatientGoals");

            migrationBuilder.DropColumn(
                name: "AuthorizationNumber",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "ConsentSigned",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "ConsentSignedDate",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "DateOfOnset",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "DiagnosisCodesJson",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "EmergencyContactName",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "EmergencyContactPhone",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "PhysicianNpi",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "ReferringPhysician",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "RetentionYears",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "LastModifiedUtc",
                table: "ObjectiveMetrics");

            migrationBuilder.DropColumn(
                name: "Side",
                table: "ObjectiveMetrics");

            migrationBuilder.DropColumn(
                name: "Unit",
                table: "ObjectiveMetrics");

            migrationBuilder.DropColumn(
                name: "IsReEvaluation",
                table: "ClinicalNotes");

            migrationBuilder.DropColumn(
                name: "NoteStatus",
                table: "ClinicalNotes");

            migrationBuilder.DropColumn(
                name: "PhysicianSignatureHash",
                table: "ClinicalNotes");

            migrationBuilder.DropColumn(
                name: "PhysicianSignedByUserId",
                table: "ClinicalNotes");

            migrationBuilder.DropColumn(
                name: "PhysicianSignedUtc",
                table: "ClinicalNotes");

            migrationBuilder.DropColumn(
                name: "TherapistNpi",
                table: "ClinicalNotes");

            migrationBuilder.DropColumn(
                name: "TotalTreatmentMinutes",
                table: "ClinicalNotes");
        }
    }
}

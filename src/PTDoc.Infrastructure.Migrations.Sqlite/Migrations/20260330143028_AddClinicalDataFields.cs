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
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ConsentSigned",
                table: "Patients",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ConsentSignedDate",
                table: "Patients",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DateOfOnset",
                table: "Patients",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DiagnosisCodesJson",
                table: "Patients",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "EmergencyContactName",
                table: "Patients",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmergencyContactPhone",
                table: "Patients",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhysicianNpi",
                table: "Patients",
                type: "TEXT",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferringPhysician",
                table: "Patients",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RetentionYears",
                table: "Patients",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastModifiedUtc",
                table: "ObjectiveMetrics",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "Side",
                table: "ObjectiveMetrics",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Unit",
                table: "ObjectiveMetrics",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsReEvaluation",
                table: "ClinicalNotes",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "NoteStatus",
                table: "ClinicalNotes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PhysicianSignatureHash",
                table: "ClinicalNotes",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PhysicianSignedByUserId",
                table: "ClinicalNotes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PhysicianSignedUtc",
                table: "ClinicalNotes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TherapistNpi",
                table: "ClinicalNotes",
                type: "TEXT",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TotalTreatmentMinutes",
                table: "ClinicalNotes",
                type: "INTEGER",
                nullable: true);
            migrationBuilder.CreateTable(
                name: "PatientGoals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PatientId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OriginatingNoteId = table.Column<Guid>(type: "TEXT", nullable: true),
                    MetByNoteId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ArchivedByNoteId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ClinicId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Timeframe = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Source = table.Column<int>(type: "INTEGER", nullable: false),
                    MatchedFunctionalLimitationId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    CompletionReason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastUpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    MetUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ArchivedUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
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

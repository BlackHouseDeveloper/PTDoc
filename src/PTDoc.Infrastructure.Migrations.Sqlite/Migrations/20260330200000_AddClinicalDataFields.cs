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
            // ── Patient additions ─────────────────────────────────────────────────
            migrationBuilder.AddColumn<string>(
                name: "ReferringPhysician",
                table: "Patients",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhysicianNpi",
                table: "Patients",
                type: "TEXT",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DateOfOnset",
                table: "Patients",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AuthorizationNumber",
                table: "Patients",
                type: "TEXT",
                nullable: true);

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

            migrationBuilder.AddColumn<string>(
                name: "DiagnosisCodesJson",
                table: "Patients",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<int>(
                name: "RetentionYears",
                table: "Patients",
                type: "INTEGER",
                nullable: true);

            // ── ClinicalNote additions ────────────────────────────────────────────
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

            migrationBuilder.AddColumn<string>(
                name: "PhysicianSignatureHash",
                table: "ClinicalNotes",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PhysicianSignedUtc",
                table: "ClinicalNotes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PhysicianSignedByUserId",
                table: "ClinicalNotes",
                type: "TEXT",
                nullable: true);

            // ── ObjectiveMetric additions ─────────────────────────────────────────
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

            migrationBuilder.AddColumn<DateTime>(
                name: "LastModifiedUtc",
                table: "ObjectiveMetrics",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ReferringPhysician", table: "Patients");
            migrationBuilder.DropColumn(name: "PhysicianNpi", table: "Patients");
            migrationBuilder.DropColumn(name: "DateOfOnset", table: "Patients");
            migrationBuilder.DropColumn(name: "AuthorizationNumber", table: "Patients");
            migrationBuilder.DropColumn(name: "EmergencyContactName", table: "Patients");
            migrationBuilder.DropColumn(name: "EmergencyContactPhone", table: "Patients");
            migrationBuilder.DropColumn(name: "ConsentSigned", table: "Patients");
            migrationBuilder.DropColumn(name: "ConsentSignedDate", table: "Patients");
            migrationBuilder.DropColumn(name: "DiagnosisCodesJson", table: "Patients");
            migrationBuilder.DropColumn(name: "RetentionYears", table: "Patients");

            migrationBuilder.DropColumn(name: "IsReEvaluation", table: "ClinicalNotes");
            migrationBuilder.DropColumn(name: "NoteStatus", table: "ClinicalNotes");
            migrationBuilder.DropColumn(name: "TherapistNpi", table: "ClinicalNotes");
            migrationBuilder.DropColumn(name: "TotalTreatmentMinutes", table: "ClinicalNotes");
            migrationBuilder.DropColumn(name: "PhysicianSignatureHash", table: "ClinicalNotes");
            migrationBuilder.DropColumn(name: "PhysicianSignedUtc", table: "ClinicalNotes");
            migrationBuilder.DropColumn(name: "PhysicianSignedByUserId", table: "ClinicalNotes");

            migrationBuilder.DropColumn(name: "Side", table: "ObjectiveMetrics");
            migrationBuilder.DropColumn(name: "Unit", table: "ObjectiveMetrics");
            migrationBuilder.DropColumn(name: "LastModifiedUtc", table: "ObjectiveMetrics");
        }
    }
}

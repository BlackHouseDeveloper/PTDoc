using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTDoc.Infrastructure.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddOutcomeMeasureResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OutcomeMeasureResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    MeasureType = table.Column<int>(type: "integer", nullable: false),
                    Score = table.Column<double>(type: "double precision", nullable: false),
                    DateRecorded = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClinicianId = table.Column<Guid>(type: "uuid", nullable: false),
                    NoteId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutcomeMeasureResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutcomeMeasureResults_ClinicalNotes_NoteId",
                        column: x => x.NoteId,
                        principalTable: "ClinicalNotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_OutcomeMeasureResults_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OutcomeMeasureResults_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutcomeMeasureResults_ClinicId",
                table: "OutcomeMeasureResults",
                column: "ClinicId",
                filter: "\"ClinicId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OutcomeMeasureResults_DateRecorded",
                table: "OutcomeMeasureResults",
                column: "DateRecorded");

            migrationBuilder.CreateIndex(
                name: "IX_OutcomeMeasureResults_NoteId",
                table: "OutcomeMeasureResults",
                column: "NoteId");

            migrationBuilder.CreateIndex(
                name: "IX_OutcomeMeasureResults_PatientId",
                table: "OutcomeMeasureResults",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_OutcomeMeasureResults_PatientId_MeasureType",
                table: "OutcomeMeasureResults",
                columns: new[] { "PatientId", "MeasureType" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OutcomeMeasureResults");
        }
    }
}

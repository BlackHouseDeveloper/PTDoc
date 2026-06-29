using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PTDoc.Infrastructure.Data;

#nullable disable

namespace PTDoc.Infrastructure.Data.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260609120000_AddPatientChartStorage")]
    public partial class AddPatientChartStorage : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PatientCommunicationLogEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PatientId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClinicId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Channel = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Direction = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Details = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    ContactName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatientCommunicationLogEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PatientCommunicationLogEntries_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PatientCommunicationLogEntries_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PatientDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PatientId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClinicId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DocumentType = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    ContentHashSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ContentBytes = table.Column<byte[]>(type: "BLOB", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    UploadedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UploadedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatientDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PatientDocuments_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PatientDocuments_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PatientCommunicationLogEntries_ClinicId",
                table: "PatientCommunicationLogEntries",
                column: "ClinicId",
                filter: "ClinicId IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PatientCommunicationLogEntries_PatientId",
                table: "PatientCommunicationLogEntries",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_PatientCommunicationLogEntries_PatientId_Channel_OccurredAtUtc",
                table: "PatientCommunicationLogEntries",
                columns: new[] { "PatientId", "Channel", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PatientCommunicationLogEntries_PatientId_OccurredAtUtc",
                table: "PatientCommunicationLogEntries",
                columns: new[] { "PatientId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PatientDocuments_ClinicId",
                table: "PatientDocuments",
                column: "ClinicId",
                filter: "ClinicId IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PatientDocuments_ContentHashSha256",
                table: "PatientDocuments",
                column: "ContentHashSha256");

            migrationBuilder.CreateIndex(
                name: "IX_PatientDocuments_PatientId",
                table: "PatientDocuments",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_PatientDocuments_PatientId_DocumentType_UploadedAtUtc",
                table: "PatientDocuments",
                columns: new[] { "PatientId", "DocumentType", "UploadedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PatientDocuments_PatientId_UploadedAtUtc",
                table: "PatientDocuments",
                columns: new[] { "PatientId", "UploadedAtUtc" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "PatientCommunicationLogEntries");
            migrationBuilder.DropTable(name: "PatientDocuments");
        }
    }
}

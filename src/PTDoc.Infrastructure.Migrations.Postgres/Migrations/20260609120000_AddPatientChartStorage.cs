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
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: true),
                    Channel = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Direction = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Summary = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Details = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ContactName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false)
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
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: true),
                    DocumentType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ContentHashSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContentBytes = table.Column<byte[]>(type: "bytea", nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    UploadedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UploadedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
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

            migrationBuilder.CreateIndex("IX_PatientCommunicationLogEntries_ClinicId", "PatientCommunicationLogEntries", "ClinicId", filter: "\"ClinicId\" IS NOT NULL");
            migrationBuilder.CreateIndex("IX_PatientCommunicationLogEntries_PatientId", "PatientCommunicationLogEntries", "PatientId");
            migrationBuilder.CreateIndex("IX_PatientCommunicationLogEntries_PatientId_Channel_OccurredAtUtc", "PatientCommunicationLogEntries", new[] { "PatientId", "Channel", "OccurredAtUtc" });
            migrationBuilder.CreateIndex("IX_PatientCommunicationLogEntries_PatientId_OccurredAtUtc", "PatientCommunicationLogEntries", new[] { "PatientId", "OccurredAtUtc" });
            migrationBuilder.CreateIndex("IX_PatientDocuments_ClinicId", "PatientDocuments", "ClinicId", filter: "\"ClinicId\" IS NOT NULL");
            migrationBuilder.CreateIndex("IX_PatientDocuments_ContentHashSha256", "PatientDocuments", "ContentHashSha256");
            migrationBuilder.CreateIndex("IX_PatientDocuments_PatientId", "PatientDocuments", "PatientId");
            migrationBuilder.CreateIndex("IX_PatientDocuments_PatientId_DocumentType_UploadedAtUtc", "PatientDocuments", new[] { "PatientId", "DocumentType", "UploadedAtUtc" });
            migrationBuilder.CreateIndex("IX_PatientDocuments_PatientId_UploadedAtUtc", "PatientDocuments", new[] { "PatientId", "UploadedAtUtc" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "PatientCommunicationLogEntries");
            migrationBuilder.DropTable(name: "PatientDocuments");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTDoc.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddComplianceSignatureEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SignatureHash",
                table: "Addendums",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ComplianceSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OverrideAttestationText = table.Column<string>(type: "text", nullable: false, defaultValue: "I acknowledge this override and attest that the justification is accurate and clinically necessary."),
                    MinJustificationLength = table.Column<int>(type: "integer", nullable: false, defaultValue: 20),
                    AllowOverrideTypes = table.Column<string>(type: "text", nullable: false, defaultValue: "[]")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComplianceSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RuleOverrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RuleName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Justification = table.Column<string>(type: "text", nullable: false),
                    AttestationText = table.Column<string>(type: "text", nullable: false),
                    TimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuleOverrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RuleOverrides_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Signatures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NoteId = table.Column<Guid>(type: "uuid", nullable: false),
                    SignedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SignatureHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AttestationText = table.Column<string>(type: "text", nullable: false),
                    ConsentAccepted = table.Column<bool>(type: "boolean", nullable: false),
                    IntentConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    IPAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    DeviceInfo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Signatures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Signatures_ClinicalNotes_NoteId",
                        column: x => x.NoteId,
                        principalTable: "ClinicalNotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Signatures_Users_SignedByUserId",
                        column: x => x.SignedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_EntityId",
                table: "AuditLogs",
                column: "EntityId",
                filter: "EntityId IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RuleOverrides_TimestampUtc",
                table: "RuleOverrides",
                column: "TimestampUtc");

            migrationBuilder.CreateIndex(
                name: "IX_RuleOverrides_UserId",
                table: "RuleOverrides",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Signatures_NoteId",
                table: "Signatures",
                column: "NoteId");

            migrationBuilder.CreateIndex(
                name: "IX_Signatures_SignedByUserId",
                table: "Signatures",
                column: "SignedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Signatures_TimestampUtc",
                table: "Signatures",
                column: "TimestampUtc");

            migrationBuilder.AddForeignKey(
                name: "FK_Addendums_Users_CreatedByUserId",
                table: "Addendums",
                column: "CreatedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Addendums_Users_CreatedByUserId",
                table: "Addendums");

            migrationBuilder.DropTable(
                name: "ComplianceSettings");

            migrationBuilder.DropTable(
                name: "RuleOverrides");

            migrationBuilder.DropTable(
                name: "Signatures");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_EntityId",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "SignatureHash",
                table: "Addendums");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PTDoc.Infrastructure.Data;

#nullable disable

namespace PTDoc.Infrastructure.Data.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260516143000_HardenCommunicationRemediation")]
    public partial class HardenCommunicationRemediation : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NormalizedPhoneNumber",
                table: "Users",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InviteToken",
                table: "IntakeForms",
                type: "TEXT",
                maxLength: 4096,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RevokedAtUtc",
                table: "PasswordResetTokens",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RevocationReason",
                table: "PasswordResetTokens",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClinicId",
                table: "CommunicationDeliveryLogs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CreatedAtUnixSeconds",
                table: "CommunicationDeliveryLogs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.Sql(
                "UPDATE CommunicationDeliveryLogs " +
                "SET CreatedAtUnixSeconds = CAST(strftime('%s', CreatedAtUtc) AS INTEGER) " +
                "WHERE CreatedAtUnixSeconds = 0");

            migrationBuilder.CreateTable(
                name: "IntakeOtpChallenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    IntakeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PatientId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClinicId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Channel = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ContactHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    OtpHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    WindowStartUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    SendCount = table.Column<int>(type: "INTEGER", nullable: false),
                    FailedVerifyCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastFailedVerifyAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ConsumedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntakeOtpChallenges", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_NormalizedPhoneNumber",
                table: "Users",
                column: "NormalizedPhoneNumber",
                filter: "NormalizedPhoneNumber IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CommunicationDeliveryLogs_ClinicId",
                table: "CommunicationDeliveryLogs",
                column: "ClinicId",
                filter: "ClinicId IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CommunicationDeliveryLogs_PatientId_Purpose_CreatedAtUnixSeconds",
                table: "CommunicationDeliveryLogs",
                columns: new[] { "PatientId", "Purpose", "CreatedAtUnixSeconds" },
                filter: "PatientId IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CommunicationDeliveryLogs_Purpose_Channel_CreatedAtUnixSeconds",
                table: "CommunicationDeliveryLogs",
                columns: new[] { "Purpose", "Channel", "CreatedAtUnixSeconds" });

            migrationBuilder.CreateIndex(
                name: "IX_CommunicationDeliveryLogs_RecipientHash_Purpose_CreatedAtUnixSeconds",
                table: "CommunicationDeliveryLogs",
                columns: new[] { "RecipientHash", "Purpose", "CreatedAtUnixSeconds" });

            migrationBuilder.CreateIndex(
                name: "IX_IntakeOtpChallenges_ClinicId",
                table: "IntakeOtpChallenges",
                column: "ClinicId",
                filter: "ClinicId IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_IntakeOtpChallenges_CorrelationId",
                table: "IntakeOtpChallenges",
                column: "CorrelationId",
                filter: "CorrelationId IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_IntakeOtpChallenges_ExpiresAtUtc",
                table: "IntakeOtpChallenges",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_IntakeOtpChallenges_IntakeId_Channel_ContactHash",
                table: "IntakeOtpChallenges",
                columns: new[] { "IntakeId", "Channel", "ContactHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntakeOtpChallenges_PatientId_Channel_UpdatedAtUtc",
                table: "IntakeOtpChallenges",
                columns: new[] { "PatientId", "Channel", "UpdatedAtUtc" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "IntakeOtpChallenges");

            migrationBuilder.DropIndex(name: "IX_Users_NormalizedPhoneNumber", table: "Users");
            migrationBuilder.DropIndex(name: "IX_CommunicationDeliveryLogs_ClinicId", table: "CommunicationDeliveryLogs");
            migrationBuilder.DropIndex(name: "IX_CommunicationDeliveryLogs_PatientId_Purpose_CreatedAtUnixSeconds", table: "CommunicationDeliveryLogs");
            migrationBuilder.DropIndex(name: "IX_CommunicationDeliveryLogs_Purpose_Channel_CreatedAtUnixSeconds", table: "CommunicationDeliveryLogs");
            migrationBuilder.DropIndex(name: "IX_CommunicationDeliveryLogs_RecipientHash_Purpose_CreatedAtUnixSeconds", table: "CommunicationDeliveryLogs");

            migrationBuilder.DropColumn(name: "NormalizedPhoneNumber", table: "Users");
            migrationBuilder.DropColumn(name: "InviteToken", table: "IntakeForms");
            migrationBuilder.DropColumn(name: "RevokedAtUtc", table: "PasswordResetTokens");
            migrationBuilder.DropColumn(name: "RevocationReason", table: "PasswordResetTokens");
            migrationBuilder.DropColumn(name: "ClinicId", table: "CommunicationDeliveryLogs");
            migrationBuilder.DropColumn(name: "CreatedAtUnixSeconds", table: "CommunicationDeliveryLogs");
        }
    }
}

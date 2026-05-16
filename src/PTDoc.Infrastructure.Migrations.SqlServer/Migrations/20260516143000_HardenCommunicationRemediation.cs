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
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InviteToken",
                table: "IntakeForms",
                type: "nvarchar(4096)",
                maxLength: 4096,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RevokedAtUtc",
                table: "PasswordResetTokens",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RevocationReason",
                table: "PasswordResetTokens",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ClinicId",
                table: "CommunicationDeliveryLogs",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CreatedAtUnixSeconds",
                table: "CommunicationDeliveryLogs",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.Sql(
                "UPDATE [CommunicationDeliveryLogs] " +
                "SET [CreatedAtUnixSeconds] = DATEDIFF_BIG(second, '1970-01-01T00:00:00+00:00', [CreatedAtUtc]) " +
                "WHERE [CreatedAtUnixSeconds] = 0");

            migrationBuilder.CreateTable(
                name: "IntakeOtpChallenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IntakeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Channel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ContactHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    OtpHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    WindowStartUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SendCount = table.Column<int>(type: "int", nullable: false),
                    FailedVerifyCount = table.Column<int>(type: "int", nullable: false),
                    LastFailedVerifyAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ConsumedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntakeOtpChallenges", x => x.Id);
                });

            migrationBuilder.CreateIndex("IX_Users_NormalizedPhoneNumber", "Users", "NormalizedPhoneNumber", filter: "NormalizedPhoneNumber IS NOT NULL");
            migrationBuilder.CreateIndex("IX_CommunicationDeliveryLogs_ClinicId", "CommunicationDeliveryLogs", "ClinicId", filter: "ClinicId IS NOT NULL");
            migrationBuilder.CreateIndex("IX_CommunicationDeliveryLogs_PatientId_Purpose_CreatedAtUnixSeconds", "CommunicationDeliveryLogs", new[] { "PatientId", "Purpose", "CreatedAtUnixSeconds" }, filter: "PatientId IS NOT NULL");
            migrationBuilder.CreateIndex("IX_CommunicationDeliveryLogs_Purpose_Channel_CreatedAtUnixSeconds", "CommunicationDeliveryLogs", new[] { "Purpose", "Channel", "CreatedAtUnixSeconds" });
            migrationBuilder.CreateIndex("IX_CommunicationDeliveryLogs_RecipientHash_Purpose_CreatedAtUnixSeconds", "CommunicationDeliveryLogs", new[] { "RecipientHash", "Purpose", "CreatedAtUnixSeconds" });
            migrationBuilder.CreateIndex("IX_IntakeOtpChallenges_ClinicId", "IntakeOtpChallenges", "ClinicId", filter: "ClinicId IS NOT NULL");
            migrationBuilder.CreateIndex("IX_IntakeOtpChallenges_CorrelationId", "IntakeOtpChallenges", "CorrelationId", filter: "CorrelationId IS NOT NULL");
            migrationBuilder.CreateIndex("IX_IntakeOtpChallenges_ExpiresAtUtc", "IntakeOtpChallenges", "ExpiresAtUtc");
            migrationBuilder.CreateIndex("IX_IntakeOtpChallenges_IntakeId_Channel_ContactHash", "IntakeOtpChallenges", new[] { "IntakeId", "Channel", "ContactHash" }, unique: true);
            migrationBuilder.CreateIndex("IX_IntakeOtpChallenges_PatientId_Channel_UpdatedAtUtc", "IntakeOtpChallenges", new[] { "PatientId", "Channel", "UpdatedAtUtc" });
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

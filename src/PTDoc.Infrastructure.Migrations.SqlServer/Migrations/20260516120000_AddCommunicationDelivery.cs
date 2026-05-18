using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PTDoc.Infrastructure.Data;

#nullable disable

namespace PTDoc.Infrastructure.Data.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260516120000_AddCommunicationDelivery")]
    public partial class AddCommunicationDelivery : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PhoneNumber",
                table: "Users",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CommunicationDeliveryLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Purpose = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RecipientHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ProviderMessageId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ErrorCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SafeErrorMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SentAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RetryCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommunicationDeliveryLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PasswordResetTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RecipientHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UsedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PasswordResetTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PasswordResetTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_PhoneNumber",
                table: "Users",
                column: "PhoneNumber",
                filter: "PhoneNumber IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CommunicationDeliveryLogs_CorrelationId",
                table: "CommunicationDeliveryLogs",
                column: "CorrelationId",
                filter: "CorrelationId IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CommunicationDeliveryLogs_PatientId",
                table: "CommunicationDeliveryLogs",
                column: "PatientId",
                filter: "PatientId IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CommunicationDeliveryLogs_PatientId_Purpose_CreatedAtUtc",
                table: "CommunicationDeliveryLogs",
                columns: new[] { "PatientId", "Purpose", "CreatedAtUtc" },
                filter: "PatientId IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CommunicationDeliveryLogs_Purpose_Channel_CreatedAtUtc",
                table: "CommunicationDeliveryLogs",
                columns: new[] { "Purpose", "Channel", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CommunicationDeliveryLogs_RecipientHash",
                table: "CommunicationDeliveryLogs",
                column: "RecipientHash");

            migrationBuilder.CreateIndex(
                name: "IX_CommunicationDeliveryLogs_UserId",
                table: "CommunicationDeliveryLogs",
                column: "UserId",
                filter: "UserId IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetTokens_CorrelationId",
                table: "PasswordResetTokens",
                column: "CorrelationId",
                filter: "CorrelationId IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetTokens_RecipientHash_CreatedAtUtc",
                table: "PasswordResetTokens",
                columns: new[] { "RecipientHash", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetTokens_TokenHash",
                table: "PasswordResetTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PasswordResetTokens_UserId_ExpiresAtUtc",
                table: "PasswordResetTokens",
                columns: new[] { "UserId", "ExpiresAtUtc" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "CommunicationDeliveryLogs");
            migrationBuilder.DropTable(name: "PasswordResetTokens");

            migrationBuilder.DropIndex(
                name: "IX_Users_PhoneNumber",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PhoneNumber",
                table: "Users");
        }
    }
}

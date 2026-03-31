using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PTDoc.Infrastructure.Data;

#nullable disable

namespace PTDoc.Infrastructure.Data.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260328000001_AddStoredRefreshToken")]
    public partial class AddStoredRefreshToken : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StoredRefreshTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TokenHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Subject = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    ClaimsJson = table.Column<string>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    IsRevoked = table.Column<bool>(type: "INTEGER", nullable: false),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoredRefreshTokens", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StoredRefreshTokens_ExpiresAtUtc",
                table: "StoredRefreshTokens",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_StoredRefreshTokens_IsRevoked",
                table: "StoredRefreshTokens",
                column: "IsRevoked");

            migrationBuilder.CreateIndex(
                name: "IX_StoredRefreshTokens_Subject",
                table: "StoredRefreshTokens",
                column: "Subject");

            migrationBuilder.CreateIndex(
                name: "IX_StoredRefreshTokens_TokenHash",
                table: "StoredRefreshTokens",
                column: "TokenHash",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "StoredRefreshTokens");
        }
    }
}

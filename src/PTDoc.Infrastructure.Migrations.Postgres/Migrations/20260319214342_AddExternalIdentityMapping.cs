using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTDoc.Infrastructure.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddExternalIdentityMapping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExternalIdentityMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ExternalSubject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    PrincipalType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    InternalEntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalIdentityMappings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIdentityMappings_IsActive",
                table: "ExternalIdentityMappings",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIdentityMappings_PrincipalType_InternalEntityId",
                table: "ExternalIdentityMappings",
                columns: new[] { "PrincipalType", "InternalEntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIdentityMappings_Provider_ExternalSubject",
                table: "ExternalIdentityMappings",
                columns: new[] { "Provider", "ExternalSubject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIdentityMappings_TenantId",
                table: "ExternalIdentityMappings",
                column: "TenantId",
                filter: "\"TenantId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExternalIdentityMappings");
        }
    }
}

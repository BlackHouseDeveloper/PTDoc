using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTDoc.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSyncConflictArchive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SyncConflictArchives",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EntityType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    EntityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DetectedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ResolutionType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    ArchivedDataJson = table.Column<string>(type: "TEXT", nullable: false),
                    ArchivedVersionLastModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ArchivedVersionModifiedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChosenDataJson = table.Column<string>(type: "TEXT", nullable: false),
                    ChosenVersionLastModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ChosenVersionModifiedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsResolved = table.Column<bool>(type: "INTEGER", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ResolvedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ResolutionNotes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncConflictArchives", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SyncConflictArchives_DetectedAt",
                table: "SyncConflictArchives",
                column: "DetectedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SyncConflictArchives_EntityType_EntityId",
                table: "SyncConflictArchives",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_SyncConflictArchives_IsResolved",
                table: "SyncConflictArchives",
                column: "IsResolved");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SyncConflictArchives");
        }
    }
}

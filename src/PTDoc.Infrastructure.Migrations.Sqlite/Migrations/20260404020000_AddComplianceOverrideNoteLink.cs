using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTDoc.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddComplianceOverrideNoteLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "NoteId",
                table: "RuleOverrides",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RuleOverrides_NoteId",
                table: "RuleOverrides",
                column: "NoteId",
                filter: "NoteId IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_RuleOverrides_ClinicalNotes_NoteId",
                table: "RuleOverrides",
                column: "NoteId",
                principalTable: "ClinicalNotes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RuleOverrides_ClinicalNotes_NoteId",
                table: "RuleOverrides");

            migrationBuilder.DropIndex(
                name: "IX_RuleOverrides_NoteId",
                table: "RuleOverrides");

            migrationBuilder.DropColumn(
                name: "NoteId",
                table: "RuleOverrides");
        }
    }
}

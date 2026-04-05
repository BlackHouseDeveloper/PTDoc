using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTDoc.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddClinicalNoteLinkedAddendums : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedUtc",
                table: "ClinicalNotes",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<bool>(
                name: "IsAddendum",
                table: "ClinicalNotes",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "ParentNoteId",
                table: "ClinicalNotes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE [ClinicalNotes]
                SET [CreatedUtc] = [LastModifiedUtc];
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalNotes_CreatedUtc",
                table: "ClinicalNotes",
                column: "CreatedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalNotes_ParentNoteId",
                table: "ClinicalNotes",
                column: "ParentNoteId");

            migrationBuilder.AddForeignKey(
                name: "FK_ClinicalNotes_ClinicalNotes_ParentNoteId",
                table: "ClinicalNotes",
                column: "ParentNoteId",
                principalTable: "ClinicalNotes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ClinicalNotes_ClinicalNotes_ParentNoteId",
                table: "ClinicalNotes");

            migrationBuilder.DropIndex(
                name: "IX_ClinicalNotes_CreatedUtc",
                table: "ClinicalNotes");

            migrationBuilder.DropIndex(
                name: "IX_ClinicalNotes_ParentNoteId",
                table: "ClinicalNotes");

            migrationBuilder.DropColumn(
                name: "CreatedUtc",
                table: "ClinicalNotes");

            migrationBuilder.DropColumn(
                name: "IsAddendum",
                table: "ClinicalNotes");

            migrationBuilder.DropColumn(
                name: "ParentNoteId",
                table: "ClinicalNotes");
        }
    }
}

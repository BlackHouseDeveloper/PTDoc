using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTDoc.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEncounterIntakeAssociation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EncounterIsReEvaluation",
                table: "IntakeForms",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "EncounterNoteId",
                table: "IntakeForms",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QuestionnaireDefinitionId",
                table: "IntakeForms",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntakeForms_EncounterNoteId",
                table: "IntakeForms",
                column: "EncounterNoteId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_IntakeForms_ClinicalNotes_EncounterNoteId",
                table: "IntakeForms",
                column: "EncounterNoteId",
                principalTable: "ClinicalNotes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_IntakeForms_ClinicalNotes_EncounterNoteId",
                table: "IntakeForms");

            migrationBuilder.DropIndex(
                name: "IX_IntakeForms_EncounterNoteId",
                table: "IntakeForms");

            migrationBuilder.DropColumn(
                name: "EncounterIsReEvaluation",
                table: "IntakeForms");

            migrationBuilder.DropColumn(
                name: "EncounterNoteId",
                table: "IntakeForms");

            migrationBuilder.DropColumn(
                name: "QuestionnaireDefinitionId",
                table: "IntakeForms");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PTDoc.Infrastructure.Data;

#nullable disable

namespace PTDoc.Infrastructure.Data.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260330110000_AddNoteTaxonomySelections")]
    public partial class AddNoteTaxonomySelections : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NoteTaxonomySelections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicalNoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CategoryId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CategoryTitle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CategoryKind = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ItemLabel = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NoteTaxonomySelections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NoteTaxonomySelections_ClinicalNotes_ClinicalNoteId",
                        column: x => x.ClinicalNoteId,
                        principalTable: "ClinicalNotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NoteTaxonomySelections_CategoryId",
                table: "NoteTaxonomySelections",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_NoteTaxonomySelections_CategoryId_ItemId",
                table: "NoteTaxonomySelections",
                columns: new[] { "CategoryId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_NoteTaxonomySelections_ClinicalNoteId",
                table: "NoteTaxonomySelections",
                column: "ClinicalNoteId");

            migrationBuilder.CreateIndex(
                name: "IX_NoteTaxonomySelections_ItemId",
                table: "NoteTaxonomySelections",
                column: "ItemId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "NoteTaxonomySelections");
        }
    }
}

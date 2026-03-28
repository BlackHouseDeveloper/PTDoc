using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PTDoc.Infrastructure.Data;

#nullable disable

namespace PTDoc.Infrastructure.Data.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260327000001_AddCoSignFields")]
    public partial class AddCoSignFields : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RequiresCoSign",
                table: "ClinicalNotes",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "CoSignedByUserId",
                table: "ClinicalNotes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CoSignedUtc",
                table: "ClinicalNotes",
                type: "TEXT",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "CoSignedUtc", table: "ClinicalNotes");
            migrationBuilder.DropColumn(name: "CoSignedByUserId", table: "ClinicalNotes");
            migrationBuilder.DropColumn(name: "RequiresCoSign", table: "ClinicalNotes");
        }
    }
}

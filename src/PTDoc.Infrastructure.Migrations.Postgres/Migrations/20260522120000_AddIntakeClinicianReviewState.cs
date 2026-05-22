using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PTDoc.Infrastructure.Data;

#nullable disable

namespace PTDoc.Infrastructure.Data.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260522120000_AddIntakeClinicianReviewState")]
    public partial class AddIntakeClinicianReviewState : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewedAtUtc",
                table: "IntakeForms",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReviewedByUserId",
                table: "IntakeForms",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntakeForms_ReviewedAtUtc",
                table: "IntakeForms",
                column: "ReviewedAtUtc");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_IntakeForms_ReviewedAtUtc",
                table: "IntakeForms");

            migrationBuilder.DropColumn(
                name: "ReviewedAtUtc",
                table: "IntakeForms");

            migrationBuilder.DropColumn(
                name: "ReviewedByUserId",
                table: "IntakeForms");
        }
    }
}

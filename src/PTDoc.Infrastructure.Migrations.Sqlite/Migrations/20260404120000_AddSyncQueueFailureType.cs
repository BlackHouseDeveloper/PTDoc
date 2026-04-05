using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using PTDoc.Infrastructure.Data;

#nullable disable

namespace PTDoc.Infrastructure.Data.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260404120000_AddSyncQueueFailureType")]
    public partial class AddSyncQueueFailureType : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FailureType",
                table: "SyncQueueItems",
                type: "INTEGER",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FailureType",
                table: "SyncQueueItems");
        }
    }
}

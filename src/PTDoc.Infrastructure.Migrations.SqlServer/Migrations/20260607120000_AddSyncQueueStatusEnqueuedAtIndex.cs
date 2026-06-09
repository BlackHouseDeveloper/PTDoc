using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PTDoc.Infrastructure.Data;

#nullable disable

namespace PTDoc.Infrastructure.Data.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260607120000_AddSyncQueueStatusEnqueuedAtIndex")]
    public partial class AddSyncQueueStatusEnqueuedAtIndex : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_SyncQueueItems_Status_EnqueuedAt",
                table: "SyncQueueItems",
                columns: new[] { "Status", "EnqueuedAt" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SyncQueueItems_Status_EnqueuedAt",
                table: "SyncQueueItems");
        }
    }
}

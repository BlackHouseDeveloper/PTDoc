using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PTDoc.Infrastructure.Data;

#nullable disable

namespace PTDoc.Infrastructure.Data.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260705010010_AddAppointmentPaymentTransactions")]
    public partial class AddAppointmentPaymentTransactions : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppointmentPaymentTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Processor = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TransactionId = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    AuthorizationCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    GatewayErrorCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    GatewayErrorMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    InvoiceNumber = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppointmentPaymentTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppointmentPaymentTransactions_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppointmentPaymentTransactions_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentPaymentTransactions_AppointmentId",
                table: "AppointmentPaymentTransactions",
                column: "AppointmentId");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentPaymentTransactions_AppointmentId_Status",
                table: "AppointmentPaymentTransactions",
                columns: new[] { "AppointmentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentPaymentTransactions_PatientId",
                table: "AppointmentPaymentTransactions",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentPaymentTransactions_TransactionId",
                table: "AppointmentPaymentTransactions",
                column: "TransactionId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppointmentPaymentTransactions");
        }
    }
}

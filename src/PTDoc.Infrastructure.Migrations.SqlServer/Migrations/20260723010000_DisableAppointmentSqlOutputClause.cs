using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PTDoc.Infrastructure.Data;

#nullable disable

namespace PTDoc.Infrastructure.Data.Migrations;

/// <summary>
/// Records the SQL Server model annotation that disables OUTPUT-based DML for
/// the trigger-backed Appointments table. The behavior is implemented by the
/// runtime EF model and intentionally requires no database DDL.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260723010000_DisableAppointmentSqlOutputClause")]
public sealed class DisableAppointmentSqlOutputClause : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Intentionally empty. TR_Appointments_PreventOverlap remains enabled.
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Intentionally empty. Rolling back migration history does not alter the trigger.
    }
}

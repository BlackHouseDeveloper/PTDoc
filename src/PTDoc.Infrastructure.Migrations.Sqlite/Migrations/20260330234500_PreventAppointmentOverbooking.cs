using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PTDoc.Infrastructure.Data;

#nullable disable

namespace PTDoc.Infrastructure.Data.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260330234500_PreventAppointmentOverbooking")]
    public partial class PreventAppointmentOverbooking : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE TRIGGER IF NOT EXISTS "TR_Appointments_PreventOverlap_Insert"
                BEFORE INSERT ON "Appointments"
                FOR EACH ROW
                WHEN NEW."Status" NOT IN (5, 6)
                BEGIN
                    SELECT RAISE(ABORT, 'APPOINTMENT_OVERBOOKING: clinician already has an overlapping appointment')
                    WHERE EXISTS (
                        SELECT 1
                        FROM "Appointments" AS existing
                        WHERE existing."ClinicalId" = NEW."ClinicalId"
                          AND existing."Id" <> NEW."Id"
                          AND existing."Status" NOT IN (5, 6)
                          AND existing."StartTimeUtc" < NEW."EndTimeUtc"
                          AND NEW."StartTimeUtc" < existing."EndTimeUtc"
                    );
                END;
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER IF NOT EXISTS "TR_Appointments_PreventOverlap_Update"
                BEFORE UPDATE ON "Appointments"
                FOR EACH ROW
                WHEN NEW."Status" NOT IN (5, 6)
                BEGIN
                    SELECT RAISE(ABORT, 'APPOINTMENT_OVERBOOKING: clinician already has an overlapping appointment')
                    WHERE EXISTS (
                        SELECT 1
                        FROM "Appointments" AS existing
                        WHERE existing."ClinicalId" = NEW."ClinicalId"
                          AND existing."Id" <> NEW."Id"
                          AND existing."Status" NOT IN (5, 6)
                          AND existing."StartTimeUtc" < NEW."EndTimeUtc"
                          AND NEW."StartTimeUtc" < existing."EndTimeUtc"
                    );
                END;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP TRIGGER IF EXISTS "TR_Appointments_PreventOverlap_Insert";""");
            migrationBuilder.Sql("""DROP TRIGGER IF EXISTS "TR_Appointments_PreventOverlap_Update";""");
        }
    }
}

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
                CREATE OR REPLACE FUNCTION "PreventAppointmentOverlap"()
                RETURNS trigger
                AS $$
                BEGIN
                    IF NEW."Status" IN (5, 6) THEN
                        RETURN NEW;
                    END IF;

                    IF EXISTS (
                        SELECT 1
                        FROM "Appointments" AS existing
                        WHERE existing."ClinicalId" = NEW."ClinicalId"
                          AND existing."Id" <> NEW."Id"
                          AND existing."Status" NOT IN (5, 6)
                          AND existing."StartTimeUtc" < NEW."EndTimeUtc"
                          AND NEW."StartTimeUtc" < existing."EndTimeUtc"
                    ) THEN
                        RAISE EXCEPTION 'APPOINTMENT_OVERBOOKING: clinician already has an overlapping appointment';
                    END IF;

                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER "TR_Appointments_PreventOverlap"
                BEFORE INSERT OR UPDATE ON "Appointments"
                FOR EACH ROW
                EXECUTE FUNCTION "PreventAppointmentOverlap"();
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP TRIGGER IF EXISTS "TR_Appointments_PreventOverlap" ON "Appointments";""");
            migrationBuilder.Sql("""DROP FUNCTION IF EXISTS "PreventAppointmentOverlap"();""");
        }
    }
}

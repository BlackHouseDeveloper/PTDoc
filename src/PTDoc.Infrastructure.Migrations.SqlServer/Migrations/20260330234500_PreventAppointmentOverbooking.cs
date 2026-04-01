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
                CREATE TRIGGER [dbo].[TR_Appointments_PreventOverlap]
                ON [dbo].[Appointments]
                AFTER INSERT, UPDATE
                AS
                BEGIN
                    SET NOCOUNT ON;

                    IF EXISTS (
                        SELECT 1
                        FROM inserted AS i
                        INNER JOIN [dbo].[Appointments] AS existing
                            ON existing.[ClinicalId] = i.[ClinicalId]
                           AND existing.[Id] <> i.[Id]
                        WHERE i.[Status] NOT IN (5, 6)
                          AND existing.[Status] NOT IN (5, 6)
                          AND existing.[StartTimeUtc] < i.[EndTimeUtc]
                          AND i.[StartTimeUtc] < existing.[EndTimeUtc]
                    )
                    BEGIN
                        THROW 51000, 'APPOINTMENT_OVERBOOKING: clinician already has an overlapping appointment', 1;
                    END
                END
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'[dbo].[TR_Appointments_PreventOverlap]', N'TR') IS NOT NULL
                    DROP TRIGGER [dbo].[TR_Appointments_PreventOverlap];
                """);
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

namespace PTDoc.Infrastructure.Data.Migrations;

public static class ClinicalVisitOrdinalMigrationOperations
{
    public static void Up(MigrationBuilder migrationBuilder, string provider)
    {
        migrationBuilder.AddColumn<int>(
            name: "ClinicalVisitOrdinal",
            table: "Appointments",
            nullable: true);

        migrationBuilder.Sql(provider switch
        {
            "Postgres" => """
                WITH ranked AS (
                    SELECT "Id", CAST(ROW_NUMBER() OVER (PARTITION BY "PatientId" ORDER BY "StartTimeUtc", "Id") AS integer) AS ordinal
                    FROM "Appointments"
                    WHERE "Status" IN (0, 1, 2, 3, 4) AND "ClinicalVisitOrdinal" IS NULL
                )
                UPDATE "Appointments" AS appointment
                SET "ClinicalVisitOrdinal" = ranked.ordinal
                FROM ranked
                WHERE appointment."Id" = ranked."Id";
                """,
            "SqlServer" => """
                WITH ranked AS (
                    SELECT [Id], ROW_NUMBER() OVER (PARTITION BY [PatientId] ORDER BY [StartTimeUtc], [Id]) AS [ordinal]
                    FROM [Appointments]
                    WHERE [Status] IN (0, 1, 2, 3, 4) AND [ClinicalVisitOrdinal] IS NULL
                )
                UPDATE appointment
                SET [ClinicalVisitOrdinal] = ranked.[ordinal]
                FROM [Appointments] AS appointment
                INNER JOIN ranked ON appointment.[Id] = ranked.[Id];
                """,
            _ => """
                WITH ranked AS (
                    SELECT Id, ROW_NUMBER() OVER (PARTITION BY PatientId ORDER BY StartTimeUtc, Id) AS ordinal
                    FROM Appointments
                    WHERE Status IN (0, 1, 2, 3, 4) AND ClinicalVisitOrdinal IS NULL
                )
                UPDATE Appointments
                SET ClinicalVisitOrdinal = (SELECT ordinal FROM ranked WHERE ranked.Id = Appointments.Id)
                WHERE Id IN (SELECT Id FROM ranked);
                """
        });

        migrationBuilder.CreateIndex(
            name: "IX_Appointments_ClinicId_PatientId_ClinicalVisitOrdinal",
            table: "Appointments",
            columns: new[] { "ClinicId", "PatientId", "ClinicalVisitOrdinal" });

        migrationBuilder.CreateIndex(
            name: "UX_Appointments_PatientId_ClinicalVisitOrdinal",
            table: "Appointments",
            columns: new[] { "PatientId", "ClinicalVisitOrdinal" },
            unique: true,
            filter: provider switch
            {
                "Postgres" => "\"ClinicalVisitOrdinal\" IS NOT NULL",
                "SqlServer" => "[ClinicalVisitOrdinal] IS NOT NULL",
                _ => "ClinicalVisitOrdinal IS NOT NULL"
            });
    }

    public static void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Appointments_ClinicId_PatientId_ClinicalVisitOrdinal",
            table: "Appointments");
        migrationBuilder.DropIndex(
            name: "UX_Appointments_PatientId_ClinicalVisitOrdinal",
            table: "Appointments");
        migrationBuilder.DropColumn(
            name: "ClinicalVisitOrdinal",
            table: "Appointments");
    }
}

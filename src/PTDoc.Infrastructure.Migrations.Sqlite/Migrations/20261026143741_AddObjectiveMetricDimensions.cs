using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTDoc.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddObjectiveMetricDimensions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MotionMode",
                table: "ObjectiveMetrics",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MovementId",
                table: "ObjectiveMetrics",
                type: "TEXT",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "ObjectiveMetrics",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormValue",
                table: "ObjectiveMetrics",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreviousValue",
                table: "ObjectiveMetrics",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferenceDocumentPath",
                table: "ObjectiveMetrics",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferenceVersion",
                table: "ObjectiveMetrics",
                type: "TEXT",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The preceding hand-authored migration has no target model for EF's
            // automatic SQLite rebuild. Preserve every legacy column and FK explicitly.
            // Export the added dimensions before downgrading: they cannot fit the old schema.
            migrationBuilder.Sql("""
                CREATE TABLE "__legacy_ObjectiveMetrics" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_ObjectiveMetrics" PRIMARY KEY,
                    "NoteId" TEXT NOT NULL,
                    "BodyPart" INTEGER NOT NULL,
                    "MetricType" INTEGER NOT NULL,
                    "Value" TEXT NOT NULL,
                    "IsWNL" INTEGER NOT NULL,
                    "LastModifiedUtc" TEXT NOT NULL DEFAULT '0001-01-01 00:00:00',
                    "Side" TEXT NULL,
                    "Unit" TEXT NULL,
                    CONSTRAINT "FK_ObjectiveMetrics_ClinicalNotes_NoteId" FOREIGN KEY ("NoteId") REFERENCES "ClinicalNotes" ("Id") ON DELETE CASCADE
                );
                INSERT INTO "__legacy_ObjectiveMetrics" ("Id", "NoteId", "BodyPart", "MetricType", "Value", "IsWNL", "LastModifiedUtc", "Side", "Unit")
                    SELECT "Id", "NoteId", "BodyPart", "MetricType", "Value", "IsWNL", "LastModifiedUtc", "Side", "Unit" FROM "ObjectiveMetrics";
                DROP TABLE "ObjectiveMetrics";
                ALTER TABLE "__legacy_ObjectiveMetrics" RENAME TO "ObjectiveMetrics";
                CREATE INDEX "IX_ObjectiveMetrics_NoteId" ON "ObjectiveMetrics" ("NoteId");
                """);
        }
    }
}

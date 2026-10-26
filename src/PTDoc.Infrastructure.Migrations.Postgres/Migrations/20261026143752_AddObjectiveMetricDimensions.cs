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
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MovementId",
                table: "ObjectiveMetrics",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "ObjectiveMetrics",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormValue",
                table: "ObjectiveMetrics",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreviousValue",
                table: "ObjectiveMetrics",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferenceDocumentPath",
                table: "ObjectiveMetrics",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferenceVersion",
                table: "ObjectiveMetrics",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.DropColumn(
                name: "MotionMode",
                table: "ObjectiveMetrics");

            migrationBuilder.DropColumn(
                name: "MovementId",
                table: "ObjectiveMetrics");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "ObjectiveMetrics");

            migrationBuilder.DropColumn(
                name: "NormValue",
                table: "ObjectiveMetrics");

            migrationBuilder.DropColumn(
                name: "PreviousValue",
                table: "ObjectiveMetrics");

            migrationBuilder.DropColumn(
                name: "ReferenceDocumentPath",
                table: "ObjectiveMetrics");

            migrationBuilder.DropColumn(
                name: "ReferenceVersion",
                table: "ObjectiveMetrics");
        }
    }
}

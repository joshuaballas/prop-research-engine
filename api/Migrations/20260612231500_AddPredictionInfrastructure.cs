using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SportsBetting.Api.Data;

#nullable disable

namespace SportsBetting.Api.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260612231500_AddPredictionInfrastructure")]
    public partial class AddPredictionInfrastructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "prediction_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    model_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    model_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_prediction_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "predictions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    prediction_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    predicted_probability = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: true),
                    predicted_value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    recommendation = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    confidence = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_predictions", x => x.id);
                    table.ForeignKey(
                        name: "fk_predictions_prediction_runs_prediction_run_id",
                        column: x => x.prediction_run_id,
                        principalTable: "prediction_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_predictions_props_prop_id",
                        column: x => x.prop_id,
                        principalTable: "props",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_prediction_runs_created_at",
                table: "prediction_runs",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_prediction_runs_model_name_model_version_created_at",
                table: "prediction_runs",
                columns: new[] { "model_name", "model_version", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_predictions_prediction_run_id",
                table: "predictions",
                column: "prediction_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_predictions_prop_id",
                table: "predictions",
                column: "prop_id");

            migrationBuilder.CreateIndex(
                name: "ix_predictions_prop_id_created_at",
                table: "predictions",
                columns: new[] { "prop_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "predictions");

            migrationBuilder.DropTable(
                name: "prediction_runs");
        }
    }
}

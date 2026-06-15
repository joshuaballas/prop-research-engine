using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SportsBetting.Api.Data;

#nullable disable

namespace SportsBetting.Api.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260613093000_AddPredictionEdgesAndSkips")]
    public partial class AddPredictionEdgesAndSkips : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "edge",
                table: "predictions",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "sportsbook_line",
                table: "predictions",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "prediction_run_skip_counts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    prediction_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_prediction_run_skip_counts", x => x.id);
                    table.ForeignKey(
                        name: "fk_prediction_run_skip_counts_prediction_runs_prediction_run_id",
                        column: x => x.prediction_run_id,
                        principalTable: "prediction_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_prediction_run_skip_counts_prediction_run_id_reason",
                table: "prediction_run_skip_counts",
                columns: new[] { "prediction_run_id", "reason" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "prediction_run_skip_counts");

            migrationBuilder.DropColumn(
                name: "edge",
                table: "predictions");

            migrationBuilder.DropColumn(
                name: "sportsbook_line",
                table: "predictions");
        }
    }
}

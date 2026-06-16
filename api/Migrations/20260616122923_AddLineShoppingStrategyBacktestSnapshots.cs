using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SportsBetting.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLineShoppingStrategyBacktestSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "line_shopping_strategy_backtest_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sport = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    slate_date = table.Column<DateOnly>(type: "date", nullable: true),
                    strategy_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    total_groups = table.Column<int>(type: "integer", nullable: false),
                    graded_groups = table.Column<int>(type: "integer", nullable: false),
                    wins = table.Column<int>(type: "integer", nullable: false),
                    losses = table.Column<int>(type: "integer", nullable: false),
                    pushes = table.Column<int>(type: "integer", nullable: false),
                    hit_rate = table.Column<decimal>(type: "numeric(8,6)", precision: 8, scale: 6, nullable: false),
                    simulated_profit = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    simulated_roi = table.Column<decimal>(type: "numeric(8,6)", precision: 8, scale: 6, nullable: false),
                    average_best_estimated_ev = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    average_best_probability = table.Column<decimal>(type: "numeric(8,6)", precision: 8, scale: 6, nullable: true),
                    average_best_implied_probability = table.Column<decimal>(type: "numeric(8,6)", precision: 8, scale: 6, nullable: true),
                    average_probability_edge = table.Column<decimal>(type: "numeric(8,6)", precision: 8, scale: 6, nullable: true),
                    average_historical_sample_size = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    average_best_price = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    generated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_line_shopping_strategy_backtest_snapshots", x => x.id);
                    table.ForeignKey(
                        name: "fk_line_shopping_strategy_backtest_snapshots_prediction_runs_r",
                        column: x => x.run_id,
                        principalTable: "prediction_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_line_shopping_strategy_backtest_snapshots_created_at",
                table: "line_shopping_strategy_backtest_snapshots",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_line_shopping_strategy_backtest_snapshots_run_id_strategy_n",
                table: "line_shopping_strategy_backtest_snapshots",
                columns: new[] { "run_id", "strategy_name", "generated_at" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_line_shopping_strategy_backtest_snapshots_sport_slate_date",
                table: "line_shopping_strategy_backtest_snapshots",
                columns: new[] { "sport", "slate_date" });

            migrationBuilder.CreateIndex(
                name: "ix_line_shopping_strategy_backtest_snapshots_strategy_name",
                table: "line_shopping_strategy_backtest_snapshots",
                column: "strategy_name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "line_shopping_strategy_backtest_snapshots");
        }
    }
}

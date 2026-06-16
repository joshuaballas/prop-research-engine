using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SportsBetting.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketReadSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "market_line_shopping_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    the_odds_refresh_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prediction_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sport = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    slate_date = table.Column<DateOnly>(type: "date", nullable: false),
                    generated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    bookmaker_preset = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    provider_category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    provider = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    bookmaker = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    player = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    event_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    event_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    event_start_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lifecycle_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    is_stale = table.Column<bool>(type: "boolean", nullable: false),
                    is_expired = table.Column<bool>(type: "boolean", nullable: false),
                    prop_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    line = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false),
                    best_side = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    best_bookmaker = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    best_american_price = table.Column<int>(type: "integer", nullable: true),
                    best_decimal_price = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: true),
                    best_probability = table.Column<decimal>(type: "numeric(8,6)", precision: 8, scale: 6, nullable: true),
                    best_implied_probability = table.Column<decimal>(type: "numeric(8,6)", precision: 8, scale: 6, nullable: true),
                    best_probability_edge = table.Column<decimal>(type: "numeric(8,6)", precision: 8, scale: 6, nullable: true),
                    best_estimated_ev = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: true),
                    strategy_classification = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    strategy_tier = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_candidate = table.Column<bool>(type: "boolean", nullable: false),
                    is_watchlist = table.Column<bool>(type: "boolean", nullable: false),
                    is_avoid = table.Column<bool>(type: "boolean", nullable: false),
                    is_research_only = table.Column<bool>(type: "boolean", nullable: false),
                    entry_provider = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    entry_compatibility_group = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    entry_provider_category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    entry_actionable_for_user = table.Column<bool>(type: "boolean", nullable: false),
                    details_json = table.Column<JsonDocument>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_market_line_shopping_snapshots", x => x.id);
                    table.ForeignKey(
                        name: "fk_market_line_shopping_snapshots_prediction_runs_prediction_r",
                        column: x => x.prediction_run_id,
                        principalTable: "prediction_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_market_line_shopping_snapshots_the_odds_refresh_runs_the_od",
                        column: x => x.the_odds_refresh_run_id,
                        principalTable: "the_odds_refresh_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "market_top_pick_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    the_odds_refresh_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prediction_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sport = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    slate_date = table.Column<DateOnly>(type: "date", nullable: false),
                    generated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    bookmaker_preset = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    provider_category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    provider = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    bookmaker = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    player = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    event_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    event_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    event_start_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lifecycle_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    is_stale = table.Column<bool>(type: "boolean", nullable: false),
                    is_expired = table.Column<bool>(type: "boolean", nullable: false),
                    prop_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    line = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false),
                    best_side = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    best_bookmaker = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    best_american_price = table.Column<int>(type: "integer", nullable: true),
                    best_decimal_price = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: true),
                    best_probability = table.Column<decimal>(type: "numeric(8,6)", precision: 8, scale: 6, nullable: true),
                    best_implied_probability = table.Column<decimal>(type: "numeric(8,6)", precision: 8, scale: 6, nullable: true),
                    best_probability_edge = table.Column<decimal>(type: "numeric(8,6)", precision: 8, scale: 6, nullable: true),
                    best_estimated_ev = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: true),
                    strategy_classification = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    strategy_tier = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_candidate = table.Column<bool>(type: "boolean", nullable: false),
                    is_watchlist = table.Column<bool>(type: "boolean", nullable: false),
                    is_avoid = table.Column<bool>(type: "boolean", nullable: false),
                    is_research_only = table.Column<bool>(type: "boolean", nullable: false),
                    entry_provider = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    entry_compatibility_group = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    entry_provider_category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    entry_actionable_for_user = table.Column<bool>(type: "boolean", nullable: false),
                    details_json = table.Column<JsonDocument>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_market_top_pick_snapshots", x => x.id);
                    table.ForeignKey(
                        name: "fk_market_top_pick_snapshots_prediction_runs_prediction_run_id",
                        column: x => x.prediction_run_id,
                        principalTable: "prediction_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_market_top_pick_snapshots_the_odds_refresh_runs_the_odds_re",
                        column: x => x.the_odds_refresh_run_id,
                        principalTable: "the_odds_refresh_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_market_line_shopping_snapshots_best_estimated_ev",
                table: "market_line_shopping_snapshots",
                column: "best_estimated_ev");

            migrationBuilder.CreateIndex(
                name: "ix_market_line_shopping_snapshots_generated_at_utc",
                table: "market_line_shopping_snapshots",
                column: "generated_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_market_line_shopping_snapshots_prediction_run_id",
                table: "market_line_shopping_snapshots",
                column: "prediction_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_market_line_shopping_snapshots_provider_category_candidate",
                table: "market_line_shopping_snapshots",
                columns: new[] { "provider_category", "is_candidate" });

            migrationBuilder.CreateIndex(
                name: "ix_market_line_shopping_snapshots_refresh_run_id",
                table: "market_line_shopping_snapshots",
                column: "the_odds_refresh_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_market_line_shopping_snapshots_sport_slate_date",
                table: "market_line_shopping_snapshots",
                columns: new[] { "sport", "slate_date" });

            migrationBuilder.CreateIndex(
                name: "ix_market_top_pick_snapshots_best_estimated_ev",
                table: "market_top_pick_snapshots",
                column: "best_estimated_ev");

            migrationBuilder.CreateIndex(
                name: "ix_market_top_pick_snapshots_generated_at_utc",
                table: "market_top_pick_snapshots",
                column: "generated_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_market_top_pick_snapshots_prediction_run_id",
                table: "market_top_pick_snapshots",
                column: "prediction_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_market_top_pick_snapshots_provider_category_candidate",
                table: "market_top_pick_snapshots",
                columns: new[] { "provider_category", "is_candidate" });

            migrationBuilder.CreateIndex(
                name: "ix_market_top_pick_snapshots_refresh_run_id",
                table: "market_top_pick_snapshots",
                column: "the_odds_refresh_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_market_top_pick_snapshots_sport_slate_date",
                table: "market_top_pick_snapshots",
                columns: new[] { "sport", "slate_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "market_line_shopping_snapshots");

            migrationBuilder.DropTable(
                name: "market_top_pick_snapshots");
        }
    }
}

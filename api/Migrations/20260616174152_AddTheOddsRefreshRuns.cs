using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SportsBetting.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTheOddsRefreshRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "the_odds_refresh_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    sport = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    slate_date = table.Column<DateOnly>(type: "date", nullable: false),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    failed = table.Column<bool>(type: "boolean", nullable: false),
                    blocked = table.Column<bool>(type: "boolean", nullable: false),
                    dry_run = table.Column<bool>(type: "boolean", nullable: false),
                    force_live = table.Column<bool>(type: "boolean", nullable: false),
                    bookmaker_preset = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    provider_categories_included = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    sportsbook_bookmakers_requested = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    dfs_bookmakers_requested = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    markets_requested = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    estimated_api_calls = table.Column<int>(type: "integer", nullable: false),
                    actual_api_calls = table.Column<int>(type: "integer", nullable: false),
                    platform_props_created = table.Column<int>(type: "integer", nullable: false),
                    platform_props_updated = table.Column<int>(type: "integer", nullable: false),
                    platform_props_upserted = table.Column<int>(type: "integer", nullable: false),
                    prediction_count = table.Column<int>(type: "integer", nullable: false),
                    line_shopping_group_count = table.Column<int>(type: "integer", nullable: false),
                    active_candidate_count = table.Column<int>(type: "integer", nullable: false),
                    active_watchlist_count = table.Column<int>(type: "integer", nullable: false),
                    active_avoid_count = table.Column<int>(type: "integer", nullable: false),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    diagnostics_json = table.Column<JsonDocument>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_the_odds_refresh_runs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_the_odds_refresh_runs_bookmaker_preset_started_at_utc",
                table: "the_odds_refresh_runs",
                columns: new[] { "bookmaker_preset", "started_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_the_odds_refresh_runs_completed_at_utc",
                table: "the_odds_refresh_runs",
                column: "completed_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_the_odds_refresh_runs_sport_slate_date",
                table: "the_odds_refresh_runs",
                columns: new[] { "sport", "slate_date" });

            migrationBuilder.CreateIndex(
                name: "ix_the_odds_refresh_runs_started_at_utc",
                table: "the_odds_refresh_runs",
                column: "started_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_the_odds_refresh_runs_succeeded_completed_at_utc",
                table: "the_odds_refresh_runs",
                columns: new[] { "succeeded", "completed_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "the_odds_refresh_runs");
        }
    }
}

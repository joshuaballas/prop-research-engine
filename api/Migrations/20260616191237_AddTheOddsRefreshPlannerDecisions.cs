using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SportsBetting.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTheOddsRefreshPlannerDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "the_odds_refresh_planner_decisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    generated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    sport = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    slate_date = table.Column<DateOnly>(type: "date", nullable: false),
                    bookmaker_preset = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    should_refresh = table.Column<bool>(type: "boolean", nullable: false),
                    skipped = table.Column<bool>(type: "boolean", nullable: false),
                    skip_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    provider_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    provider_available = table.Column<bool>(type: "boolean", nullable: false),
                    provider_rejected_key_detected = table.Column<bool>(type: "boolean", nullable: false),
                    requests_used = table.Column<int>(type: "integer", nullable: true),
                    requests_remaining = table.Column<int>(type: "integer", nullable: true),
                    estimated_api_calls = table.Column<int>(type: "integer", nullable: false),
                    daily_credits_used = table.Column<int>(type: "integer", nullable: false),
                    monthly_credits_used = table.Column<int>(type: "integer", nullable: false),
                    daily_credit_limit = table.Column<int>(type: "integer", nullable: false),
                    monthly_credit_limit = table.Column<int>(type: "integer", nullable: false),
                    budget_allowed = table.Column<bool>(type: "boolean", nullable: false),
                    cadence_rule = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    minutes_until_next_event_start = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    minutes_since_last_successful_refresh = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    next_eligible_refresh_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recommended_action = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    details_json = table.Column<JsonDocument>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_the_odds_refresh_planner_decisions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_the_odds_refresh_planner_decisions_generated_at_utc",
                table: "the_odds_refresh_planner_decisions",
                column: "generated_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_the_odds_refresh_planner_decisions_skipped_generated_at_utc",
                table: "the_odds_refresh_planner_decisions",
                columns: new[] { "skipped", "generated_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_the_odds_refresh_planner_decisions_source_generated_at_utc",
                table: "the_odds_refresh_planner_decisions",
                columns: new[] { "source", "generated_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_the_odds_refresh_planner_decisions_sport_generated_at_utc",
                table: "the_odds_refresh_planner_decisions",
                columns: new[] { "sport", "generated_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "the_odds_refresh_planner_decisions");
        }
    }
}

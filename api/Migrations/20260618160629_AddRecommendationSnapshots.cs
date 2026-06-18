using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SportsBetting.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRecommendationSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "recommendation_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    first_seen_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    seen_count = table.Column<int>(type: "integer", nullable: false),
                    captured_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    refresh_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    prediction_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sport = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    slate_date = table.Column<DateOnly>(type: "date", nullable: false),
                    expected_result_date = table.Column<DateOnly>(type: "date", nullable: false),
                    result_date_source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    event_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    event_start_time_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    canonical_player_id = table.Column<Guid>(type: "uuid", nullable: true),
                    player_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    normalized_player_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    prop_type_id = table.Column<Guid>(type: "uuid", nullable: true),
                    prop_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    normalized_prop_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    side = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    line = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false),
                    provider = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    bookmaker = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    provider_category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    entry_provider = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    entry_compatibility_group = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    hit_chance = table.Column<decimal>(type: "numeric(8,6)", precision: 8, scale: 6, nullable: true),
                    implied_probability = table.Column<decimal>(type: "numeric(8,6)", precision: 8, scale: 6, nullable: true),
                    probability_edge = table.Column<decimal>(type: "numeric(8,6)", precision: 8, scale: 6, nullable: true),
                    estimated_ev = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: true),
                    trust_level = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    sample_warning = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    warning = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    historical_sample_size = table.Column<int>(type: "integer", nullable: true),
                    strategy_classification = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    strategy_tier = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ranking_score = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    is_candidate = table.Column<bool>(type: "boolean", nullable: false),
                    is_top_pick = table.Column<bool>(type: "boolean", nullable: false),
                    is_watchlist = table.Column<bool>(type: "boolean", nullable: false),
                    is_actionable_for_user = table.Column<bool>(type: "boolean", nullable: false),
                    lifecycle_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    result_value = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    grade_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    graded_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    grading_failure_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recommendation_snapshots", x => x.id);
                    table.ForeignKey(
                        name: "fk_recommendation_snapshots_players_canonical_player_id",
                        column: x => x.canonical_player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_recommendation_snapshots_prediction_runs_prediction_run_id",
                        column: x => x.prediction_run_id,
                        principalTable: "prediction_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_recommendation_snapshots_prop_types_prop_type_id",
                        column: x => x.prop_type_id,
                        principalTable: "prop_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_recommendation_snapshots_the_odds_refresh_runs_refresh_run_",
                        column: x => x.refresh_run_id,
                        principalTable: "the_odds_refresh_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_recommendation_snapshots_canonical_player_id",
                table: "recommendation_snapshots",
                column: "canonical_player_id");

            migrationBuilder.CreateIndex(
                name: "ix_recommendation_snapshots_grade_lookup",
                table: "recommendation_snapshots",
                columns: new[] { "expected_result_date", "canonical_player_id", "prop_type_id" });

            migrationBuilder.CreateIndex(
                name: "ix_recommendation_snapshots_grade_status",
                table: "recommendation_snapshots",
                column: "grade_status");

            migrationBuilder.CreateIndex(
                name: "ix_recommendation_snapshots_idempotency_key",
                table: "recommendation_snapshots",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_recommendation_snapshots_prediction_run_id",
                table: "recommendation_snapshots",
                column: "prediction_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_recommendation_snapshots_prop_type_id",
                table: "recommendation_snapshots",
                column: "prop_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_recommendation_snapshots_provider_candidate",
                table: "recommendation_snapshots",
                columns: new[] { "provider_category", "is_candidate" });

            migrationBuilder.CreateIndex(
                name: "ix_recommendation_snapshots_refresh_run_id",
                table: "recommendation_snapshots",
                column: "refresh_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_recommendation_snapshots_sport_expected_result_date",
                table: "recommendation_snapshots",
                columns: new[] { "sport", "expected_result_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recommendation_snapshots");
        }
    }
}

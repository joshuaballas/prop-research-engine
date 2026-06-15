using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SportsBetting.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:uuid-ossp", ",,");

            migrationBuilder.CreateTable(
                name: "platforms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platforms", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sports", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ingestion_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    platform_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    props_seen = table.Column<int>(type: "integer", nullable: false),
                    props_changed = table.Column<int>(type: "integer", nullable: false),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    payload_size_bytes = table.Column<long>(type: "bigint", nullable: true),
                    payload_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    schema_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    warning_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ingestion_runs", x => x.id);
                    table.ForeignKey(
                        name: "fk_ingestion_runs_platforms_platform_id",
                        column: x => x.platform_id,
                        principalTable: "platforms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "optimized_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    platform_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entry_size = table.Column<int>(type: "integer", nullable: false),
                    combined_probability = table.Column<decimal>(type: "numeric(8,6)", precision: 8, scale: 6, nullable: false),
                    expected_value = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: false),
                    confidence_score = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    correlation_score = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    risk_score = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    rank = table.Column<int>(type: "integer", nullable: false),
                    model_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    generated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_optimized_entries", x => x.id);
                    table.ForeignKey(
                        name: "fk_optimized_entries_platforms_platform_id",
                        column: x => x.platform_id,
                        principalTable: "platforms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "leagues",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sport_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    season_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leagues", x => x.id);
                    table.ForeignKey(
                        name: "fk_leagues_sports_sport_id",
                        column: x => x.sport_id,
                        principalTable: "sports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "prop_types",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sport_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    stat_key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    value_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_prop_types", x => x.id);
                    table.ForeignKey(
                        name: "fk_prop_types_sports_sport_id",
                        column: x => x.sport_id,
                        principalTable: "sports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "raw_ingestion_payloads",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    ingestion_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    platform_id = table.Column<Guid>(type: "uuid", nullable: false),
                    endpoint = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    payload_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    payload = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_raw_ingestion_payloads", x => x.id);
                    table.ForeignKey(
                        name: "fk_raw_ingestion_payloads_ingestion_runs_ingestion_run_id",
                        column: x => x.ingestion_run_id,
                        principalTable: "ingestion_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_raw_ingestion_payloads_platforms_platform_id",
                        column: x => x.platform_id,
                        principalTable: "platforms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "teams",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    league_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    abbreviation = table.Column<string>(type: "character varying(25)", maxLength: 25, nullable: true),
                    external_refs = table.Column<JsonDocument>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_teams", x => x.id);
                    table.ForeignKey(
                        name: "fk_teams_leagues_league_id",
                        column: x => x.league_id,
                        principalTable: "leagues",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "correlation_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    league_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prop_type_a_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prop_type_b_id = table.Column<Guid>(type: "uuid", nullable: false),
                    context = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    expected_direction = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    default_weight = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_correlation_rules", x => x.id);
                    table.ForeignKey(
                        name: "fk_correlation_rules_leagues_league_id",
                        column: x => x.league_id,
                        principalTable: "leagues",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_correlation_rules_prop_types_prop_type_a_id",
                        column: x => x.prop_type_a_id,
                        principalTable: "prop_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_correlation_rules_prop_types_prop_type_b_id",
                        column: x => x.prop_type_b_id,
                        principalTable: "prop_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "games",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    league_id = table.Column<Guid>(type: "uuid", nullable: false),
                    home_team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    away_team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scheduled_start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    external_refs = table.Column<JsonDocument>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_games", x => x.id);
                    table.ForeignKey(
                        name: "fk_games_leagues_league_id",
                        column: x => x.league_id,
                        principalTable: "leagues",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_games_teams_away_team_id",
                        column: x => x.away_team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_games_teams_home_team_id",
                        column: x => x.home_team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "players",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    league_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    external_refs = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_players", x => x.id);
                    table.ForeignKey(
                        name: "fk_players_leagues_league_id",
                        column: x => x.league_id,
                        principalTable: "leagues",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_players_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "props",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    game_id = table.Column<Guid>(type: "uuid", nullable: true),
                    prop_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    league_id = table.Column<Guid>(type: "uuid", nullable: false),
                    normalized_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_props", x => x.id);
                    table.ForeignKey(
                        name: "fk_props_games_game_id",
                        column: x => x.game_id,
                        principalTable: "games",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_props_leagues_league_id",
                        column: x => x.league_id,
                        principalTable: "leagues",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_props_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_props_prop_types_prop_type_id",
                        column: x => x.prop_type_id,
                        principalTable: "prop_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "correlation_pairs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    prop_a_id = table.Column<Guid>(type: "uuid", nullable: true),
                    prop_b_id = table.Column<Guid>(type: "uuid", nullable: true),
                    prop_type_a_id = table.Column<Guid>(type: "uuid", nullable: true),
                    prop_type_b_id = table.Column<Guid>(type: "uuid", nullable: true),
                    relationship_scope = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    correlation_coefficient = table.Column<decimal>(type: "numeric(8,6)", precision: 8, scale: 6, nullable: false),
                    confidence_score = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    sample_size = table.Column<int>(type: "integer", nullable: false),
                    model_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    calculated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_correlation_pairs", x => x.id);
                    table.ForeignKey(
                        name: "fk_correlation_pairs_prop_types_prop_type_a_id",
                        column: x => x.prop_type_a_id,
                        principalTable: "prop_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_correlation_pairs_prop_types_prop_type_b_id",
                        column: x => x.prop_type_b_id,
                        principalTable: "prop_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_correlation_pairs_props_prop_a_id",
                        column: x => x.prop_a_id,
                        principalTable: "props",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_correlation_pairs_props_prop_b_id",
                        column: x => x.prop_b_id,
                        principalTable: "props",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "platform_props",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    prop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    platform_id = table.Column<Guid>(type: "uuid", nullable: false),
                    platform_prop_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    external_stable_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    label = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    current_line = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false),
                    over_available = table.Column<bool>(type: "boolean", nullable: false),
                    under_available = table.Column<bool>(type: "boolean", nullable: false),
                    payout_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    raw_payload_id = table.Column<Guid>(type: "uuid", nullable: true),
                    metadata = table.Column<JsonDocument>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_props", x => x.id);
                    table.ForeignKey(
                        name: "fk_platform_props_platforms_platform_id",
                        column: x => x.platform_id,
                        principalTable: "platforms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_platform_props_props_prop_id",
                        column: x => x.prop_id,
                        principalTable: "props",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_platform_props_raw_ingestion_payloads_raw_payload_id",
                        column: x => x.raw_payload_id,
                        principalTable: "raw_ingestion_payloads",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "line_movements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    platform_prop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    previous_line = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false),
                    current_line = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false),
                    delta = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false),
                    direction = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    movement_percent = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    detected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    significance_score = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_line_movements", x => x.id);
                    table.ForeignKey(
                        name: "fk_line_movements_platform_props_platform_prop_id",
                        column: x => x.platform_prop_id,
                        principalTable: "platform_props",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "optimized_entry_legs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    optimized_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    platform_prop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    side = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    leg_probability = table.Column<decimal>(type: "numeric(8,6)", precision: 8, scale: 6, nullable: false),
                    leg_expected_value = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: false),
                    leg_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_optimized_entry_legs", x => x.id);
                    table.ForeignKey(
                        name: "fk_optimized_entry_legs_optimized_entries_optimized_entry_id",
                        column: x => x.optimized_entry_id,
                        principalTable: "optimized_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_optimized_entry_legs_platform_props_platform_prop_id",
                        column: x => x.platform_prop_id,
                        principalTable: "platform_props",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "prop_analysis",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    platform_prop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    side = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    hit_probability = table.Column<decimal>(type: "numeric(8,6)", precision: 8, scale: 6, nullable: false),
                    fair_line = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false),
                    expected_value = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: false),
                    confidence_score = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    historical_hit_rate = table.Column<decimal>(type: "numeric(8,6)", precision: 8, scale: 6, nullable: false),
                    line_movement_score = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    platform_edge_score = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    model_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    explanation = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    calculated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_prop_analysis", x => x.id);
                    table.ForeignKey(
                        name: "fk_prop_analysis_platform_props_platform_prop_id",
                        column: x => x.platform_prop_id,
                        principalTable: "platform_props",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "prop_line_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    platform_prop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false),
                    over_available = table.Column<bool>(type: "boolean", nullable: false),
                    under_available = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    source_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    raw_payload_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_prop_line_snapshots", x => x.id);
                    table.ForeignKey(
                        name: "fk_prop_line_snapshots_platform_props_platform_prop_id",
                        column: x => x.platform_prop_id,
                        principalTable: "platform_props",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_prop_line_snapshots_raw_ingestion_payloads_raw_payload_id",
                        column: x => x.raw_payload_id,
                        principalTable: "raw_ingestion_payloads",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_correlation_pairs_prop_a_id_prop_b_id_model_version",
                table: "correlation_pairs",
                columns: new[] { "prop_a_id", "prop_b_id", "model_version" });

            migrationBuilder.CreateIndex(
                name: "ix_correlation_pairs_prop_b_id",
                table: "correlation_pairs",
                column: "prop_b_id");

            migrationBuilder.CreateIndex(
                name: "ix_correlation_pairs_prop_type_a_id_prop_type_b_id_relationshi",
                table: "correlation_pairs",
                columns: new[] { "prop_type_a_id", "prop_type_b_id", "relationship_scope" });

            migrationBuilder.CreateIndex(
                name: "ix_correlation_pairs_prop_type_b_id",
                table: "correlation_pairs",
                column: "prop_type_b_id");

            migrationBuilder.CreateIndex(
                name: "ix_correlation_rules_league_id_prop_type_a_id_prop_type_b_id_c",
                table: "correlation_rules",
                columns: new[] { "league_id", "prop_type_a_id", "prop_type_b_id", "context" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_correlation_rules_prop_type_a_id",
                table: "correlation_rules",
                column: "prop_type_a_id");

            migrationBuilder.CreateIndex(
                name: "ix_correlation_rules_prop_type_b_id",
                table: "correlation_rules",
                column: "prop_type_b_id");

            migrationBuilder.CreateIndex(
                name: "ix_games_away_team_id",
                table: "games",
                column: "away_team_id");

            migrationBuilder.CreateIndex(
                name: "ix_games_home_team_id",
                table: "games",
                column: "home_team_id");

            migrationBuilder.CreateIndex(
                name: "ix_games_league_id",
                table: "games",
                column: "league_id");

            migrationBuilder.CreateIndex(
                name: "ix_games_scheduled_start_at",
                table: "games",
                column: "scheduled_start_at");

            migrationBuilder.CreateIndex(
                name: "ix_ingestion_runs_platform_id_started_at",
                table: "ingestion_runs",
                columns: new[] { "platform_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ix_leagues_sport_id_code",
                table: "leagues",
                columns: new[] { "sport_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_line_movements_detected_at",
                table: "line_movements",
                column: "detected_at");

            migrationBuilder.CreateIndex(
                name: "ix_line_movements_platform_prop_id_detected_at",
                table: "line_movements",
                columns: new[] { "platform_prop_id", "detected_at" });

            migrationBuilder.CreateIndex(
                name: "ix_optimized_entries_entry_size_rank",
                table: "optimized_entries",
                columns: new[] { "entry_size", "rank" });

            migrationBuilder.CreateIndex(
                name: "ix_optimized_entries_generated_at",
                table: "optimized_entries",
                column: "generated_at");

            migrationBuilder.CreateIndex(
                name: "ix_optimized_entries_platform_id",
                table: "optimized_entries",
                column: "platform_id");

            migrationBuilder.CreateIndex(
                name: "ix_optimized_entry_legs_optimized_entry_id_leg_order",
                table: "optimized_entry_legs",
                columns: new[] { "optimized_entry_id", "leg_order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_optimized_entry_legs_platform_prop_id",
                table: "optimized_entry_legs",
                column: "platform_prop_id");

            migrationBuilder.CreateIndex(
                name: "ix_platform_props_external_stable_key",
                table: "platform_props",
                column: "external_stable_key");

            migrationBuilder.CreateIndex(
                name: "ix_platform_props_observed_at",
                table: "platform_props",
                column: "observed_at");

            migrationBuilder.CreateIndex(
                name: "ix_platform_props_platform_id_platform_prop_id",
                table: "platform_props",
                columns: new[] { "platform_id", "platform_prop_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_platform_props_prop_id",
                table: "platform_props",
                column: "prop_id");

            migrationBuilder.CreateIndex(
                name: "ix_platform_props_raw_payload_id",
                table: "platform_props",
                column: "raw_payload_id");

            migrationBuilder.CreateIndex(
                name: "ix_platforms_slug",
                table: "platforms",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_players_league_id_display_name",
                table: "players",
                columns: new[] { "league_id", "display_name" });

            migrationBuilder.CreateIndex(
                name: "ix_players_team_id",
                table: "players",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_prop_analysis_platform_prop_id_side_model_version",
                table: "prop_analysis",
                columns: new[] { "platform_prop_id", "side", "model_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_prop_line_snapshots_observed_at",
                table: "prop_line_snapshots",
                column: "observed_at");

            migrationBuilder.CreateIndex(
                name: "ix_prop_line_snapshots_platform_prop_id_line_observed_at",
                table: "prop_line_snapshots",
                columns: new[] { "platform_prop_id", "line", "observed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_prop_line_snapshots_platform_prop_id_observed_at",
                table: "prop_line_snapshots",
                columns: new[] { "platform_prop_id", "observed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_prop_line_snapshots_raw_payload_id",
                table: "prop_line_snapshots",
                column: "raw_payload_id");

            migrationBuilder.CreateIndex(
                name: "ix_prop_types_sport_id_stat_key",
                table: "prop_types",
                columns: new[] { "sport_id", "stat_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_props_game_id",
                table: "props",
                column: "game_id");

            migrationBuilder.CreateIndex(
                name: "ix_props_league_id",
                table: "props",
                column: "league_id");

            migrationBuilder.CreateIndex(
                name: "ix_props_normalized_key",
                table: "props",
                column: "normalized_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_props_player_id_game_id_prop_type_id",
                table: "props",
                columns: new[] { "player_id", "game_id", "prop_type_id" });

            migrationBuilder.CreateIndex(
                name: "ix_props_prop_type_id",
                table: "props",
                column: "prop_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_raw_ingestion_payloads_captured_at",
                table: "raw_ingestion_payloads",
                column: "captured_at");

            migrationBuilder.CreateIndex(
                name: "ix_raw_ingestion_payloads_ingestion_run_id",
                table: "raw_ingestion_payloads",
                column: "ingestion_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_raw_ingestion_payloads_payload_hash",
                table: "raw_ingestion_payloads",
                column: "payload_hash");

            migrationBuilder.CreateIndex(
                name: "ix_raw_ingestion_payloads_platform_id",
                table: "raw_ingestion_payloads",
                column: "platform_id");

            migrationBuilder.CreateIndex(
                name: "ix_sports_code",
                table: "sports",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_teams_league_id_abbreviation",
                table: "teams",
                columns: new[] { "league_id", "abbreviation" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "correlation_pairs");

            migrationBuilder.DropTable(
                name: "correlation_rules");

            migrationBuilder.DropTable(
                name: "line_movements");

            migrationBuilder.DropTable(
                name: "optimized_entry_legs");

            migrationBuilder.DropTable(
                name: "prop_analysis");

            migrationBuilder.DropTable(
                name: "prop_line_snapshots");

            migrationBuilder.DropTable(
                name: "optimized_entries");

            migrationBuilder.DropTable(
                name: "platform_props");

            migrationBuilder.DropTable(
                name: "props");

            migrationBuilder.DropTable(
                name: "raw_ingestion_payloads");

            migrationBuilder.DropTable(
                name: "games");

            migrationBuilder.DropTable(
                name: "players");

            migrationBuilder.DropTable(
                name: "prop_types");

            migrationBuilder.DropTable(
                name: "ingestion_runs");

            migrationBuilder.DropTable(
                name: "teams");

            migrationBuilder.DropTable(
                name: "platforms");

            migrationBuilder.DropTable(
                name: "leagues");

            migrationBuilder.DropTable(
                name: "sports");
        }
    }
}

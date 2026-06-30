using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SportsBetting.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddProviderDomainConfigTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "provider_bookmakers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    bookmaker_key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    provider_category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    region = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    is_expanded_coverage = table.Column<bool>(type: "boolean", nullable: false),
                    supports_line_shopping = table.Column<bool>(type: "boolean", nullable: false),
                    supports_entry_builder = table.Column<bool>(type: "boolean", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_provider_bookmakers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "provider_competitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    sport = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    competition_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    competition_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    provider_league_id = table.Column<int>(type: "integer", nullable: true),
                    provider_sport_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    supports_results_import = table.Column<bool>(type: "boolean", nullable: false),
                    supports_odds_import = table.Column<bool>(type: "boolean", nullable: false),
                    supports_season_metadata = table.Column<bool>(type: "boolean", nullable: false),
                    is_fixed_season = table.Column<bool>(type: "boolean", nullable: false),
                    fixed_season = table.Column<int>(type: "integer", nullable: true),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_provider_competitions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "provider_markets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    sport = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    competition_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    provider_category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    market_key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    internal_prop_type = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    supports_odds_import = table.Column<bool>(type: "boolean", nullable: false),
                    supports_line_shopping = table.Column<bool>(type: "boolean", nullable: false),
                    supports_top_picks = table.Column<bool>(type: "boolean", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_provider_markets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "provider_refresh_targets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    target_key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    sport = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    competition_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    competition_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    provider_category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    bookmaker_preset = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    provider_sport_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    regions = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    max_events = table.Column<int>(type: "integer", nullable: true),
                    estimated_api_calls = table.Column<int>(type: "integer", nullable: true),
                    supports_results = table.Column<bool>(type: "boolean", nullable: false),
                    supports_history = table.Column<bool>(type: "boolean", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    disabled_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_provider_refresh_targets", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_provider_bookmakers_bookmaker_key",
                table: "provider_bookmakers",
                column: "bookmaker_key");

            migrationBuilder.CreateIndex(
                name: "ix_provider_bookmakers_provider_key_bookmaker_key_provider_cat",
                table: "provider_bookmakers",
                columns: new[] { "provider_key", "bookmaker_key", "provider_category", "region" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_provider_bookmakers_provider_key_provider_category_is_enabl",
                table: "provider_bookmakers",
                columns: new[] { "provider_key", "provider_category", "is_enabled" });

            migrationBuilder.CreateIndex(
                name: "ix_provider_competitions_is_enabled",
                table: "provider_competitions",
                column: "is_enabled");

            migrationBuilder.CreateIndex(
                name: "ix_provider_competitions_provider_key_sport",
                table: "provider_competitions",
                columns: new[] { "provider_key", "sport" });

            migrationBuilder.CreateIndex(
                name: "ix_provider_competitions_provider_key_sport_competition_key",
                table: "provider_competitions",
                columns: new[] { "provider_key", "sport", "competition_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_provider_competitions_provider_league_id",
                table: "provider_competitions",
                column: "provider_league_id");

            migrationBuilder.CreateIndex(
                name: "ix_provider_markets_market_key",
                table: "provider_markets",
                column: "market_key");

            migrationBuilder.CreateIndex(
                name: "ix_provider_markets_provider_key_sport_competition_key_provide",
                table: "provider_markets",
                columns: new[] { "provider_key", "sport", "competition_key", "provider_category", "market_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_provider_markets_provider_key_sport_is_enabled",
                table: "provider_markets",
                columns: new[] { "provider_key", "sport", "is_enabled" });

            migrationBuilder.CreateIndex(
                name: "ix_provider_refresh_targets_priority",
                table: "provider_refresh_targets",
                column: "priority");

            migrationBuilder.CreateIndex(
                name: "ix_provider_refresh_targets_provider_key_is_enabled",
                table: "provider_refresh_targets",
                columns: new[] { "provider_key", "is_enabled" });

            migrationBuilder.CreateIndex(
                name: "ix_provider_refresh_targets_provider_key_target_key",
                table: "provider_refresh_targets",
                columns: new[] { "provider_key", "target_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_provider_refresh_targets_sport_competition_key_provider_cat",
                table: "provider_refresh_targets",
                columns: new[] { "sport", "competition_key", "provider_category" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "provider_bookmakers");

            migrationBuilder.DropTable(
                name: "provider_competitions");

            migrationBuilder.DropTable(
                name: "provider_markets");

            migrationBuilder.DropTable(
                name: "provider_refresh_targets");
        }
    }
}

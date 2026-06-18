using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SportsBetting.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddProviderLineLifecycles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "provider_line_lifecycles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sport = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    provider_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    provider_display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    provider_category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    event_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    event_start_time_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    slate_date = table.Column<DateOnly>(type: "date", nullable: true),
                    canonical_player_id = table.Column<Guid>(type: "uuid", nullable: true),
                    provider_player_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    normalized_player_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    market_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    prop_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    side = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    current_line = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    current_price = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: true),
                    current_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    first_seen_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_changed_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_refresh_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    last_snapshot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    seen_count = table.Column<int>(type: "integer", nullable: false),
                    changed_count = table.Column<int>(type: "integer", nullable: false),
                    missing_count = table.Column<int>(type: "integer", nullable: false),
                    pulled_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    locked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expired_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_provider_line_lifecycles", x => x.id);
                    table.ForeignKey(
                        name: "fk_provider_line_lifecycles_the_odds_refresh_runs_last_refresh",
                        column: x => x.last_refresh_run_id,
                        principalTable: "the_odds_refresh_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "provider_line_lifecycle_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_line_lifecycle_id = table.Column<Guid>(type: "uuid", nullable: false),
                    refresh_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    event_type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    previous_line = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    new_line = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    previous_price = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: true),
                    new_price = table.Column<decimal>(type: "numeric(12,6)", precision: 12, scale: 6, nullable: true),
                    previous_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    new_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    metadata_json = table.Column<JsonDocument>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_provider_line_lifecycle_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_provider_line_lifecycle_events_provider_line_lifecycles_pro",
                        column: x => x.provider_line_lifecycle_id,
                        principalTable: "provider_line_lifecycles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_provider_line_lifecycle_events_the_odds_refresh_runs_refres",
                        column: x => x.refresh_run_id,
                        principalTable: "the_odds_refresh_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_provider_line_lifecycle_events_event_type",
                table: "provider_line_lifecycle_events",
                column: "event_type");

            migrationBuilder.CreateIndex(
                name: "ix_provider_line_lifecycle_events_lifecycle_id",
                table: "provider_line_lifecycle_events",
                column: "provider_line_lifecycle_id");

            migrationBuilder.CreateIndex(
                name: "ix_provider_line_lifecycle_events_occurred_at_utc",
                table: "provider_line_lifecycle_events",
                column: "occurred_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_provider_line_lifecycle_events_refresh_run_id",
                table: "provider_line_lifecycle_events",
                column: "refresh_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_provider_line_lifecycles_event_start_time_utc",
                table: "provider_line_lifecycles",
                column: "event_start_time_utc");

            migrationBuilder.CreateIndex(
                name: "ix_provider_line_lifecycles_first_seen_utc",
                table: "provider_line_lifecycles",
                column: "first_seen_utc");

            migrationBuilder.CreateIndex(
                name: "ix_provider_line_lifecycles_last_changed_utc",
                table: "provider_line_lifecycles",
                column: "last_changed_utc");

            migrationBuilder.CreateIndex(
                name: "ix_provider_line_lifecycles_last_refresh_run_id",
                table: "provider_line_lifecycles",
                column: "last_refresh_run_id");

            migrationBuilder.CreateIndex(
                name: "ix_provider_line_lifecycles_last_seen_utc",
                table: "provider_line_lifecycles",
                column: "last_seen_utc");

            migrationBuilder.CreateIndex(
                name: "ix_provider_line_lifecycles_lookup",
                table: "provider_line_lifecycles",
                columns: new[] { "sport", "provider_key", "provider_category", "event_id", "normalized_player_name", "market_key", "side" });

            migrationBuilder.CreateIndex(
                name: "ix_provider_line_lifecycles_provider_status",
                table: "provider_line_lifecycles",
                columns: new[] { "sport", "provider_key", "current_status" });

            migrationBuilder.CreateIndex(
                name: "ix_provider_line_lifecycles_status",
                table: "provider_line_lifecycles",
                column: "current_status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "provider_line_lifecycle_events");

            migrationBuilder.DropTable(
                name: "provider_line_lifecycles");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SportsBetting.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSoccerResultImportWorkerState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "soccer_result_import_fixture_states",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    competition = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    fixture_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    event_date = table.Column<DateOnly>(type: "date", nullable: true),
                    kickoff_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    home_team_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    away_team_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    status_short = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    status_long = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    import_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    skip_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    result_rows_previewed = table.Column<int>(type: "integer", nullable: false),
                    result_rows_written = table.Column<int>(type: "integer", nullable: false),
                    result_rows_inserted = table.Column<int>(type: "integer", nullable: false),
                    result_rows_updated = table.Column<int>(type: "integer", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_attempted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_successful_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error_message = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_soccer_result_import_fixture_states", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "soccer_result_import_states",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    competition = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    last_successful_import_date = table.Column<DateOnly>(type: "date", nullable: true),
                    last_successful_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    last_error_message = table.Column<string>(type: "text", nullable: true),
                    latest_completed_fixture_detected_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    latest_completed_fixture_detected_date = table.Column<DateOnly>(type: "date", nullable: true),
                    latest_completed_fixture_imported_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    latest_completed_fixture_imported_date = table.Column<DateOnly>(type: "date", nullable: true),
                    last_api_calls_used = table.Column<int>(type: "integer", nullable: false),
                    last_discovered_fixture_count = table.Column<int>(type: "integer", nullable: false),
                    last_completed_fixture_count = table.Column<int>(type: "integer", nullable: false),
                    last_imported_fixture_count = table.Column<int>(type: "integer", nullable: false),
                    last_failed_fixture_count = table.Column<int>(type: "integer", nullable: false),
                    last_skipped_pending_fixture_count = table.Column<int>(type: "integer", nullable: false),
                    last_result_rows_written = table.Column<int>(type: "integer", nullable: false),
                    last_result_rows_inserted = table.Column<int>(type: "integer", nullable: false),
                    last_result_rows_updated = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_soccer_result_import_states", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_soccer_result_import_fixture_states_competition_event_date",
                table: "soccer_result_import_fixture_states",
                columns: new[] { "competition", "event_date" });

            migrationBuilder.CreateIndex(
                name: "ix_soccer_result_import_fixture_states_import_status_event_date",
                table: "soccer_result_import_fixture_states",
                columns: new[] { "import_status", "event_date" });

            migrationBuilder.CreateIndex(
                name: "ix_soccer_result_import_fixture_states_source_competition_fixt",
                table: "soccer_result_import_fixture_states",
                columns: new[] { "source", "competition", "fixture_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_soccer_result_import_states_last_successful_import_date",
                table: "soccer_result_import_states",
                column: "last_successful_import_date");

            migrationBuilder.CreateIndex(
                name: "ix_soccer_result_import_states_source_competition",
                table: "soccer_result_import_states",
                columns: new[] { "source", "competition" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "soccer_result_import_fixture_states");

            migrationBuilder.DropTable(
                name: "soccer_result_import_states");
        }
    }
}

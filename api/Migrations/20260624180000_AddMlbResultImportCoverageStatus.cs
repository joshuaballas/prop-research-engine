using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SportsBetting.Api.Data;

#nullable disable

namespace SportsBetting.Api.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260624180000_AddMlbResultImportCoverageStatus")]
    public partial class AddMlbResultImportCoverageStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "completed_games_expected",
                table: "mlb_result_import_date_states",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "completed_games_imported",
                table: "mlb_result_import_date_states",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "imported_results",
                table: "mlb_result_import_date_states",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "distinct_players",
                table: "mlb_result_import_date_states",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "distinct_prop_types",
                table: "mlb_result_import_date_states",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "coverage_status",
                table: "mlb_result_import_date_states",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "NotStarted");

            migrationBuilder.AddColumn<string>(
                name: "coverage_reason",
                table: "mlb_result_import_date_states",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "latest_import_attempt_utc",
                table: "mlb_result_import_date_states",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "latest_successful_repair_utc",
                table: "mlb_result_import_date_states",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_mlb_result_import_date_states_coverage_status_event_date",
                table: "mlb_result_import_date_states",
                columns: new[] { "coverage_status", "event_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_mlb_result_import_date_states_coverage_status_event_date",
                table: "mlb_result_import_date_states");

            migrationBuilder.DropColumn(
                name: "completed_games_expected",
                table: "mlb_result_import_date_states");

            migrationBuilder.DropColumn(
                name: "completed_games_imported",
                table: "mlb_result_import_date_states");

            migrationBuilder.DropColumn(
                name: "imported_results",
                table: "mlb_result_import_date_states");

            migrationBuilder.DropColumn(
                name: "distinct_players",
                table: "mlb_result_import_date_states");

            migrationBuilder.DropColumn(
                name: "distinct_prop_types",
                table: "mlb_result_import_date_states");

            migrationBuilder.DropColumn(
                name: "coverage_status",
                table: "mlb_result_import_date_states");

            migrationBuilder.DropColumn(
                name: "coverage_reason",
                table: "mlb_result_import_date_states");

            migrationBuilder.DropColumn(
                name: "latest_import_attempt_utc",
                table: "mlb_result_import_date_states");

            migrationBuilder.DropColumn(
                name: "latest_successful_repair_utc",
                table: "mlb_result_import_date_states");
        }
    }
}

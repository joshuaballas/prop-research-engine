using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SportsBetting.Api.Data;

#nullable disable

namespace SportsBetting.Api.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260613170000_AddMlbResultImportState")]
    public partial class AddMlbResultImportState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mlb_result_import_states",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    last_successful_import_date = table.Column<DateOnly>(type: "date", nullable: true),
                    last_successful_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    last_error_message = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mlb_result_import_states", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "mlb_result_import_date_states",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_attempted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_successful_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error_message = table.Column<string>(type: "text", nullable: true),
                    records_found = table.Column<int>(type: "integer", nullable: false),
                    records_mapped = table.Column<int>(type: "integer", nullable: false),
                    records_inserted = table.Column<int>(type: "integer", nullable: false),
                    records_updated = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mlb_result_import_date_states", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_mlb_result_import_date_states_event_date",
                table: "mlb_result_import_date_states",
                column: "event_date",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mlb_result_import_date_states_status_event_date",
                table: "mlb_result_import_date_states",
                columns: new[] { "status", "event_date" });

            migrationBuilder.CreateIndex(
                name: "ix_mlb_result_import_states_last_successful_import_date",
                table: "mlb_result_import_states",
                column: "last_successful_import_date");

            migrationBuilder.CreateIndex(
                name: "ix_mlb_result_import_states_source",
                table: "mlb_result_import_states",
                column: "source",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mlb_result_import_date_states");

            migrationBuilder.DropTable(
                name: "mlb_result_import_states");
        }
    }
}

using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SportsBetting.Api.Data;

#nullable disable

namespace SportsBetting.Api.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260615090000_AddApplicationErrors")]
    public partial class AddApplicationErrors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "application_errors",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    severity = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    source = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    component = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    operation = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    request_path = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    http_method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    status_code = table.Column<int>(type: "integer", nullable: true),
                    message = table.Column<string>(type: "text", nullable: false),
                    exception_type = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    stack_trace = table.Column<string>(type: "text", nullable: true),
                    inner_exception = table.Column<string>(type: "text", nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    metadata_json = table.Column<JsonDocument>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_application_errors", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_application_errors_component",
                table: "application_errors",
                column: "component");

            migrationBuilder.CreateIndex(
                name: "ix_application_errors_correlation_id",
                table: "application_errors",
                column: "correlation_id");

            migrationBuilder.CreateIndex(
                name: "ix_application_errors_occurred_at_utc",
                table: "application_errors",
                column: "occurred_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_application_errors_severity",
                table: "application_errors",
                column: "severity");

            migrationBuilder.CreateIndex(
                name: "ix_application_errors_source",
                table: "application_errors",
                column: "source");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "application_errors");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SportsBetting.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAppConfigs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppConfigs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    value = table.Column<string>(type: "text", nullable: false),
                    value_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    default_value = table.Column<string>(type: "text", nullable: true),
                    is_sensitive = table.Column<bool>(type: "boolean", nullable: false),
                    is_runtime_editable = table.Column<bool>(type: "boolean", nullable: false),
                    requires_restart = table.Column<bool>(type: "boolean", nullable: false),
                    validation_json = table.Column<string>(type: "text", nullable: true),
                    environment = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    row_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_app_configs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_app_configs_category",
                table: "AppConfigs",
                column: "category");

            migrationBuilder.CreateIndex(
                name: "ix_app_configs_is_enabled",
                table: "AppConfigs",
                column: "is_enabled");

            migrationBuilder.CreateIndex(
                name: "ix_app_configs_key_environment",
                table: "AppConfigs",
                columns: new[] { "key", "environment" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppConfigs");
        }
    }
}

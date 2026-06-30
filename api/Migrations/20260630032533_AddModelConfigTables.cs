using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SportsBetting.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddModelConfigTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "model_config_sets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    model_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    model_era = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    sport = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_model_config_sets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "model_config_parameters",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    model_config_set_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parameter_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    value_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    value = table.Column<string>(type: "text", nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    unit = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_model_config_parameters", x => x.id);
                    table.ForeignKey(
                        name: "fk_model_config_parameters_model_config_sets_model_config_set_",
                        column: x => x.model_config_set_id,
                        principalTable: "model_config_sets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_model_config_parameters_model_config_set_id_parameter_key",
                table: "model_config_parameters",
                columns: new[] { "model_config_set_id", "parameter_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_model_config_parameters_parameter_key",
                table: "model_config_parameters",
                column: "parameter_key");

            migrationBuilder.CreateIndex(
                name: "ix_model_config_sets_is_active",
                table: "model_config_sets",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_model_config_sets_model_key_model_era_sport",
                table: "model_config_sets",
                columns: new[] { "model_key", "model_era", "sport" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_model_config_sets_model_key_sport_is_active",
                table: "model_config_sets",
                columns: new[] { "model_key", "sport", "is_active" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "model_config_parameters");

            migrationBuilder.DropTable(
                name: "model_config_sets");
        }
    }
}

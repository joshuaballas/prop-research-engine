using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SportsBetting.Api.Data;

#nullable disable

namespace SportsBetting.Api.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260612114000_AddPropResults")]
    public partial class AddPropResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "prop_results",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    league_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prop_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_date = table.Column<DateOnly>(type: "date", nullable: false),
                    actual_result_value = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false),
                    external_source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    external_source_identifier = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_prop_results", x => x.id);
                    table.ForeignKey(
                        name: "fk_prop_results_leagues_league_id",
                        column: x => x.league_id,
                        principalTable: "leagues",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_prop_results_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_prop_results_prop_types_prop_type_id",
                        column: x => x.prop_type_id,
                        principalTable: "prop_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_prop_results_external_source_external_source_identifier",
                table: "prop_results",
                columns: new[] { "external_source", "external_source_identifier" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_prop_results_league_id_event_date",
                table: "prop_results",
                columns: new[] { "league_id", "event_date" });

            migrationBuilder.CreateIndex(
                name: "ix_prop_results_player_id_prop_type_id_event_date",
                table: "prop_results",
                columns: new[] { "player_id", "prop_type_id", "event_date" });

            migrationBuilder.CreateIndex(
                name: "ix_prop_results_prop_type_id",
                table: "prop_results",
                column: "prop_type_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "prop_results");
        }
    }
}

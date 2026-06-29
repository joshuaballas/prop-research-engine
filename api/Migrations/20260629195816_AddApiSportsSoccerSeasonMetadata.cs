using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SportsBetting.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddApiSportsSoccerSeasonMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "api_sports_soccer_season_metadata",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    league_id = table.Column<int>(type: "integer", nullable: false),
                    league_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    season = table.Column<int>(type: "integer", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: true),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    is_current = table.Column<bool>(type: "boolean", nullable: false),
                    last_refreshed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    raw_season_json = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_api_sports_soccer_season_metadata", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_api_sports_soccer_season_metadata_last_refreshed_at_utc",
                table: "api_sports_soccer_season_metadata",
                column: "last_refreshed_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_api_sports_soccer_season_metadata_league_id_season",
                table: "api_sports_soccer_season_metadata",
                columns: new[] { "league_id", "season" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_api_sports_soccer_season_metadata_league_id_start_date_end_",
                table: "api_sports_soccer_season_metadata",
                columns: new[] { "league_id", "start_date", "end_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "api_sports_soccer_season_metadata");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SportsBetting.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddProviderRefreshTargetIntervals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "refresh_interval_minutes",
                table: "provider_refresh_targets",
                type: "integer",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.Sql("""
                UPDATE provider_refresh_targets
                SET refresh_interval_minutes = 15
                WHERE target_key IN ('mlb-dfs', 'mlb-sportsbook');
                """);

            migrationBuilder.Sql("""
                UPDATE provider_refresh_targets
                SET refresh_interval_minutes = 30
                WHERE sport = 'SOCCER';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "refresh_interval_minutes",
                table: "provider_refresh_targets");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SportsBetting.Api.Migrations
{
    /// <inheritdoc />
    public partial class RemoveStaleAppConfigScalars : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM "AppConfigs"
                WHERE "key" IN (
                    'Soccer.Results.ApiSports.Worker.CompetitionKeys',
                    'ApiSports.Football.WorldCupLeagueId',
                    'ApiSports.Football.Season',
                    'TheOdds.RefreshPlanner.FarOutMinMinutes',
                    'TheOdds.RefreshPlanner.SameDayMinMinutes',
                    'TheOdds.RefreshPlanner.PreLockMinMinutes',
                    'TheOdds.RefreshPlanner.NearLockMinMinutes'
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}

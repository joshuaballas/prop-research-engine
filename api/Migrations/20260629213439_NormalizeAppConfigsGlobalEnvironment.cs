using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SportsBetting.Api.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeAppConfigsGlobalEnvironment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "AppConfigs" existing
                SET environment = 'Global'
                WHERE existing.environment = 'All'
                  AND NOT EXISTS (
                      SELECT 1
                      FROM "AppConfigs" global_row
                      WHERE global_row.key = existing.key
                        AND global_row.environment = 'Global'
                  );
                """);

            migrationBuilder.Sql(
                """
                DELETE FROM "AppConfigs" legacy
                USING "AppConfigs" global_row
                WHERE legacy.environment = 'All'
                  AND global_row.environment = 'Global'
                  AND legacy.key = global_row.key
                  AND legacy.value = global_row.value;
                """);

            migrationBuilder.Sql(
                """
                INSERT INTO "AppConfigs" (
                    id,
                    key,
                    value,
                    value_type,
                    category,
                    description,
                    default_value,
                    is_sensitive,
                    is_runtime_editable,
                    requires_restart,
                    validation_json,
                    environment,
                    is_enabled,
                    created_at_utc,
                    updated_at_utc,
                    updated_by,
                    row_version)
                SELECT
                    uuid_generate_v4(),
                    source.key,
                    source.value,
                    source.value_type,
                    source.category,
                    source.description,
                    source.default_value,
                    source.is_sensitive,
                    source.is_runtime_editable,
                    source.requires_restart,
                    source.validation_json,
                    'Global',
                    source.is_enabled,
                    source.created_at_utc,
                    now(),
                    'NormalizeAppConfigsGlobalEnvironment',
                    source.row_version
                FROM (
                    SELECT DISTINCT ON (key) *
                    FROM "AppConfigs"
                    WHERE environment NOT IN ('Global', 'All')
                    ORDER BY key,
                        CASE WHEN environment = 'Production' THEN 0 ELSE 1 END,
                        updated_at_utc DESC
                ) AS source
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM "AppConfigs" global_row
                    WHERE global_row.key = source.key
                      AND global_row.environment = 'Global'
                );
                """);

            migrationBuilder.Sql(
                """
                DELETE FROM "AppConfigs" environment_row
                USING "AppConfigs" global_row
                WHERE global_row.environment = 'Global'
                  AND environment_row.environment <> 'Global'
                  AND environment_row.key = global_row.key
                  AND environment_row.value = global_row.value
                  AND environment_row.value_type = global_row.value_type;
                """);

            migrationBuilder.Sql(
                """
                DELETE FROM "AppConfigs"
                WHERE key IN (
                    'ApiSports.Football.WorldCupLeagueId',
                    'ApiSports.Football.Season',
                    'TheOdds.RefreshPlanner.FarOutMinMinutes',
                    'TheOdds.RefreshPlanner.SameDayMinMinutes',
                    'TheOdds.RefreshPlanner.PreLockMinMinutes',
                    'TheOdds.RefreshPlanner.NearLockMinMinutes',
                    'TheOdds.Api.Markets',
                    'TheOdds.Api.SoccerWorldCupSportKey',
                    'TheOdds.Api.SoccerWorldCupMarkets',
                    'TheOdds.IngestionWorker.Bookmakers',
                    'TheOdds.IngestionWorker.ExpandedBookmakers',
                    'TheOdds.IngestionWorker.DfsBookmakers',
                    'TheOdds.IngestionWorker.Markets',
                    'Soccer.Results.ApiSports.Worker.CompetitionKeys'
                )
                AND (
                    updated_by IN ('AppConfigSeeder', 'NormalizeAppConfigsGlobalEnvironment')
                    OR value IS NULL
                    OR btrim(value) = ''
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data cleanup migration only. Restoring duplicated environment rows would
            // risk overwriting operator edits, so downgrade intentionally leaves data as-is.
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SportsBetting.Api.Migrations
{
    /// <inheritdoc />
    public partial class SeedDevelopmentScheduledRefreshEnabledAppConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                    'TheOdds.ScheduledRefresh.Enabled',
                    'true',
                    'Boolean',
                    'TheOddsScheduledRefresh',
                    'Enables the scheduled one-clock The Odds refresh worker.',
                    'false',
                    false,
                    true,
                    false,
                    NULL,
                    'Development',
                    true,
                    now(),
                    now(),
                    'SeedDevelopmentScheduledRefreshEnabledAppConfig',
                    replace(uuid_generate_v4()::text, '-', '')
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM "AppConfigs"
                    WHERE key = 'TheOdds.ScheduledRefresh.Enabled'
                      AND environment = 'Development'
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM "AppConfigs"
                WHERE key = 'TheOdds.ScheduledRefresh.Enabled'
                  AND environment = 'Development'
                  AND updated_by = 'SeedDevelopmentScheduledRefreshEnabledAppConfig'
                  AND value = 'true';
                """);
        }
    }
}

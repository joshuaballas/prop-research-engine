using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SportsBetting.Api.Data;

#nullable disable

namespace SportsBetting.Api.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260630143000_SeedDevelopmentMlbResultWorkerEnabledAppConfig")]
    public partial class SeedDevelopmentMlbResultWorkerEnabledAppConfig : Migration
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
                    'Mlb.Results.Worker.Enabled',
                    'true',
                    'Boolean',
                    'MlbResults',
                    'Enables MLB result import worker.',
                    'true',
                    false,
                    true,
                    false,
                    NULL,
                    'Development',
                    true,
                    now(),
                    now(),
                    'SeedDevelopmentMlbResultWorkerEnabledAppConfig',
                    replace(uuid_generate_v4()::text, '-', '')
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM "AppConfigs"
                    WHERE key = 'Mlb.Results.Worker.Enabled'
                      AND environment = 'Development'
                );

                UPDATE "AppConfigs"
                SET value = 'true',
                    updated_at_utc = now(),
                    updated_by = 'SeedDevelopmentMlbResultWorkerEnabledAppConfig',
                    row_version = replace(uuid_generate_v4()::text, '-', '')
                WHERE key = 'Mlb.Results.Worker.Enabled'
                  AND environment = 'Development'
                  AND lower(value) IN ('false', '0', 'no', 'off')
                  AND updated_by IN (
                      'AppConfigSeeder',
                      'NormalizeAppConfigsGlobalEnvironment',
                      'SeedDevelopmentMlbResultWorkerEnabledAppConfig'
                  );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM "AppConfigs"
                WHERE key = 'Mlb.Results.Worker.Enabled'
                  AND environment = 'Development'
                  AND updated_by = 'SeedDevelopmentMlbResultWorkerEnabledAppConfig'
                  AND value = 'true';
                """);
        }
    }
}

using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SportsBetting.Api.Data;

#nullable disable

namespace SportsBetting.Api.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260621193000_AddProviderStatMappings")]
    public partial class AddProviderStatMappings : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "provider_stat_mappings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sport_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    provider_product = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    competition_scope = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    season = table.Column<int>(type: "integer", nullable: true),
                    provider_mapping_key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    provider_prop_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    provider_stat_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    provider_stat_fields_json = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    canonical_prop_type_id = table.Column<Guid>(type: "uuid", nullable: true),
                    canonical_stat_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    value_transform = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    rule_confidence = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    is_gradeable = table.Column<bool>(type: "boolean", nullable: false),
                    public_grading_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    metadata_json = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_provider_stat_mappings", x => x.id);
                    table.ForeignKey(
                        name: "fk_provider_stat_mappings_prop_types_canonical_prop_type_id",
                        column: x => x.canonical_prop_type_id,
                        principalTable: "prop_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_provider_stat_mappings_sports_sport_id",
                        column: x => x.sport_id,
                        principalTable: "sports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_provider_stat_mappings_canonical_prop_type_id",
                table: "provider_stat_mappings",
                column: "canonical_prop_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_provider_stat_mappings_canonical_stat_key",
                table: "provider_stat_mappings",
                column: "canonical_stat_key");

            migrationBuilder.CreateIndex(
                name: "ix_provider_stat_mappings_confidence_gradeable",
                table: "provider_stat_mappings",
                columns: new[] { "rule_confidence", "is_gradeable" });

            migrationBuilder.CreateIndex(
                name: "ix_provider_stat_mappings_provider_sport_active",
                table: "provider_stat_mappings",
                columns: new[] { "provider_key", "sport_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_provider_stat_mappings_sport_id",
                table: "provider_stat_mappings",
                column: "sport_id");

            migrationBuilder.CreateIndex(
                name: "ux_provider_stat_mappings_active_mapping_key",
                table: "provider_stat_mappings",
                column: "provider_mapping_key",
                unique: true);

            migrationBuilder.Sql("""
                INSERT INTO provider_stat_mappings (
                    id,
                    sport_id,
                    provider_key,
                    provider_product,
                    competition_scope,
                    season,
                    provider_mapping_key,
                    provider_prop_name,
                    provider_stat_key,
                    provider_stat_fields_json,
                    canonical_prop_type_id,
                    canonical_stat_key,
                    display_name,
                    value_transform,
                    rule_confidence,
                    is_gradeable,
                    public_grading_enabled,
                    is_active,
                    notes,
                    metadata_json,
                    created_at_utc,
                    updated_at_utc)
                SELECT
                    uuid_generate_v4(),
                    sports.id,
                    seed.provider_key,
                    seed.provider_product,
                    seed.competition_scope,
                    seed.season,
                    seed.provider_mapping_key,
                    seed.provider_prop_name,
                    seed.provider_stat_key,
                    seed.provider_stat_fields_json::jsonb,
                    prop_types.id,
                    seed.canonical_stat_key,
                    seed.display_name,
                    seed.value_transform,
                    seed.rule_confidence,
                    seed.is_gradeable,
                    false,
                    true,
                    seed.notes,
                    jsonb_build_object('seedSource', 'AddProviderStatMappings', 'provider', 'API-Sports', 'product', 'API-Sports Football'),
                    now(),
                    now()
                FROM (
                    VALUES
                        ('api-sports-football', 'API-Sports Football', 'World Cup', 2026, 'api-sports-football:SOCCER:World Cup:2026:GOALS', 'Goals', 'goals.total', '["goals.total"]', 'GOALS', 'Goals', 'Direct', 'Known', true, 'Maps API-Sports Football goals.total.'),
                        ('api-sports-football', 'API-Sports Football', 'World Cup', 2026, 'api-sports-football:SOCCER:World Cup:2026:ASSISTS', 'Assists', 'goals.assists', '["goals.assists"]', 'ASSISTS', 'Assists', 'Direct', 'Known', true, 'Maps API-Sports Football goals.assists.'),
                        ('api-sports-football', 'API-Sports Football', 'World Cup', 2026, 'api-sports-football:SOCCER:World Cup:2026:SHOTS', 'Shots', 'shots.total', '["shots.total"]', 'SHOTS', 'Shots', 'Direct', 'Known', true, 'Maps API-Sports Football shots.total.'),
                        ('api-sports-football', 'API-Sports Football', 'World Cup', 2026, 'api-sports-football:SOCCER:World Cup:2026:SHOTS_ON_TARGET', 'Shots On Target', 'shots.on', '["shots.on"]', 'SHOTS_ON_TARGET', 'Shots On Target', 'Direct', 'Known', true, 'Maps API-Sports Football shots.on.'),
                        ('api-sports-football', 'API-Sports Football', 'World Cup', 2026, 'api-sports-football:SOCCER:World Cup:2026:PASSES_ATTEMPTED', 'Passes Attempted', 'passes.total', '["passes.total"]', 'PASSES_ATTEMPTED', 'Passes Attempted', 'Direct', 'Known', true, 'Maps API-Sports Football passes.total.'),
                        ('api-sports-football', 'API-Sports Football', 'World Cup', 2026, 'api-sports-football:SOCCER:World Cup:2026:TACKLES', 'Tackles', 'tackles.total', '["tackles.total"]', 'TACKLES', 'Tackles', 'Direct', 'Known', true, 'Maps API-Sports Football tackles.total.'),
                        ('api-sports-football', 'API-Sports Football', 'World Cup', 2026, 'api-sports-football:SOCCER:World Cup:2026:FOULS', 'Fouls', 'fouls.committed', '["fouls.committed"]', 'FOULS', 'Fouls', 'Direct', 'Known', true, 'Maps API-Sports Football fouls.committed.'),
                        ('api-sports-football', 'API-Sports Football', 'World Cup', 2026, 'api-sports-football:SOCCER:World Cup:2026:FOULS_DRAWN', 'Fouls Drawn', 'fouls.drawn', '["fouls.drawn"]', 'FOULS_DRAWN', 'Fouls Drawn', 'Direct', 'Known', true, 'Maps API-Sports Football fouls.drawn.'),
                        ('api-sports-football', 'API-Sports Football', 'World Cup', 2026, 'api-sports-football:SOCCER:World Cup:2026:OFFSIDES', 'Offsides', 'offsides', '["offsides"]', 'OFFSIDES', 'Offsides', 'Direct', 'Known', true, 'Maps API-Sports Football offsides.'),
                        ('api-sports-football', 'API-Sports Football', 'World Cup', 2026, 'api-sports-football:SOCCER:World Cup:2026:GOALIE_SAVES', 'Goalie Saves', 'goals.saves', '["goals.saves"]', 'GOALIE_SAVES', 'Goalie Saves', 'GoalkeeperOnlyDirect', 'Known', true, 'Goalkeeper-only mapping from API-Sports Football goals.saves.'),
                        ('api-sports-football', 'API-Sports Football', 'World Cup', 2026, 'api-sports-football:SOCCER:World Cup:2026:ATTEMPTED_DRIBBLES', 'Attempted Dribbles', 'dribbles.attempts', '["dribbles.attempts"]', 'ATTEMPTED_DRIBBLES', 'Attempted Dribbles', 'Direct', 'Known', true, 'Maps API-Sports Football dribbles.attempts.'),
                        ('api-sports-football', 'API-Sports Football', 'World Cup', 2026, 'api-sports-football:SOCCER:World Cup:2026:GOAL_ASSIST', 'Goal + Assist', NULL, '["goals.total","goals.assists"]', 'GOAL_ASSIST', 'Goal + Assist', 'Sum', 'NeedsConfirmation', false, 'Conservative mapping: sum goals.total + goals.assists.'),
                        ('api-sports-football', 'API-Sports Football', 'World Cup', 2026, 'api-sports-football:SOCCER:World Cup:2026:CARDS', 'Cards', NULL, '["cards.yellow","cards.red"]', 'CARDS', 'Cards', 'Sum', 'NeedsConfirmation', false, 'Conservative mapping: sum cards.yellow + cards.red.'),
                        ('api-sports-football', 'API-Sports Football', 'World Cup', 2026, 'api-sports-football:SOCCER:World Cup:2026:GOALS_ALLOWED', 'Goals Allowed', 'goals.conceded', '["goals.conceded"]', 'GOALS_ALLOWED', 'Goals Allowed', 'GoalkeeperOnlyDirect', 'NeedsConfirmation', false, 'Conservative goalkeeper-only mapping from goals.conceded.'),
                        ('api-sports-football', 'API-Sports Football', 'World Cup', 2026, 'api-sports-football:SOCCER:World Cup:2026:SHOTS_ASSISTED', 'Shots Assisted', 'passes.key', '["passes.key"]', 'SHOTS_ASSISTED', 'Shots Assisted', 'Direct', 'NeedsConfirmation', false, 'passes.key may correspond to shots assisted/key passes.'),
                        ('api-sports-football', 'API-Sports Football', 'World Cup', 2026, 'api-sports-football:SOCCER:World Cup:2026:CROSSES', 'Crosses', NULL, '[]', 'CROSSES', 'Crosses', 'Unsupported', 'Unsupported', false, 'Unsupported until API-Sports Football provides a confirmed player crosses field.'),
                        ('api-sports-football', 'API-Sports Football', 'World Cup', 2026, 'api-sports-football:SOCCER:World Cup:2026:CLEARANCES', 'Clearances', NULL, '[]', 'CLEARANCES', 'Clearances', 'Unsupported', 'Unsupported', false, 'Unsupported until API-Sports Football provides a confirmed player clearances field.'),
                        ('api-sports-football', 'API-Sports Football', 'World Cup', 2026, 'api-sports-football:SOCCER:World Cup:2026:OUTFIELD_FANTASY_SCORE', 'Outfield Fantasy Score', NULL, '[]', 'OUTFIELD_FANTASY_SCORE', 'Outfield Fantasy Score', 'Unsupported', 'Unsupported', false, 'Unsupported provider-scored prop.'),
                        ('api-sports-football', 'API-Sports Football', 'World Cup', 2026, 'api-sports-football:SOCCER:World Cup:2026:GOALIE_FANTASY_SCORE', 'Goalie Fantasy Score', NULL, '[]', 'GOALIE_FANTASY_SCORE', 'Goalie Fantasy Score', 'Unsupported', 'Unsupported', false, 'Unsupported provider-scored prop.'),
                        ('api-sports-football', 'API-Sports Football', 'World Cup', 2026, 'api-sports-football:SOCCER:World Cup:2026:COMBO_PROPS', 'Combo props', NULL, '[]', 'COMBO_PROPS', 'Combo props', 'Unsupported', 'Unsupported', false, 'Unsupported combo prop family.')
                ) AS seed(provider_key, provider_product, competition_scope, season, provider_mapping_key, provider_prop_name, provider_stat_key, provider_stat_fields_json, canonical_stat_key, display_name, value_transform, rule_confidence, is_gradeable, notes)
                JOIN sports ON sports.code = 'SOCCER'
                LEFT JOIN prop_types ON prop_types.sport_id = sports.id AND prop_types.stat_key = seed.canonical_stat_key
                ON CONFLICT DO NOTHING;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "provider_stat_mappings");
        }
    }
}

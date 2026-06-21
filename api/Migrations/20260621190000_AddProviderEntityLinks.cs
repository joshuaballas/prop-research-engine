using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SportsBetting.Api.Data;

#nullable disable

namespace SportsBetting.Api.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260621190000_AddProviderEntityLinks")]
    public partial class AddProviderEntityLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "provider_entity_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sport_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entity_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    canonical_entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    canonical_entity_kind = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    canonical_entity_name = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    provider_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    provider_entity_id = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    provider_entity_name = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    provider_team_id = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    provider_team_name = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    provider_competition_id = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    provider_competition_name = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    provider_event_id = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    event_start_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    confidence = table.Column<decimal>(type: "numeric(6,4)", nullable: true),
                    link_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    link_source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    metadata_json = table.Column<string>(type: "jsonb", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    approved_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    approved_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    rejected_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    rejected_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_provider_entity_links", x => x.id);
                    table.ForeignKey(
                        name: "fk_provider_entity_links_sports_sport_id",
                        column: x => x.sport_id,
                        principalTable: "sports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_provider_entity_links_canonical_entity_id",
                table: "provider_entity_links",
                column: "canonical_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_provider_entity_links_provider_identity",
                table: "provider_entity_links",
                columns: new[] { "provider_key", "entity_type", "provider_entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_provider_entity_links_provider_identity_status",
                table: "provider_entity_links",
                columns: new[] { "provider_key", "entity_type", "provider_entity_id", "sport_id", "link_status" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_provider_entity_links_sport_entity_status",
                table: "provider_entity_links",
                columns: new[] { "sport_id", "entity_type", "link_status" });

            migrationBuilder.Sql(
                """
                INSERT INTO provider_entity_links (
                    id,
                    sport_id,
                    entity_type,
                    canonical_entity_id,
                    canonical_entity_kind,
                    canonical_entity_name,
                    provider_key,
                    provider_entity_id,
                    provider_entity_name,
                    provider_team_id,
                    provider_team_name,
                    provider_competition_id,
                    provider_competition_name,
                    provider_event_id,
                    event_start_utc,
                    confidence,
                    link_status,
                    link_source,
                    metadata_json,
                    created_at_utc,
                    updated_at_utc,
                    approved_at_utc,
                    approved_by,
                    rejected_at_utc,
                    rejected_reason)
                SELECT
                    uuid_generate_v4(),
                    sports.id,
                    'Player',
                    NULL,
                    NULL,
                    NULL,
                    'api-sports-football',
                    split_part(prop_results.external_source_identifier, ':', 3),
                    max(players.display_name),
                    NULL,
                    max(teams.name),
                    '1',
                    'World Cup',
                    min(split_part(prop_results.external_source_identifier, ':', 2)),
                    NULL,
                    NULL,
                    'Candidate',
                    'Migration',
                    jsonb_build_object(
                        'provider', 'API-Sports',
                        'product', 'API-Sports Football',
                        'migrationSource', 'ExistingPropResults',
                        'resultRowCount', count(*),
                        'fixtureIds', jsonb_agg(DISTINCT split_part(prop_results.external_source_identifier, ':', 2))
                    ),
                    min(prop_results.created_at),
                    max(prop_results.updated_at),
                    NULL,
                    NULL,
                    NULL,
                    NULL
                FROM prop_results
                JOIN players ON players.id = prop_results.player_id
                JOIN leagues ON leagues.id = prop_results.league_id
                JOIN sports ON sports.id = leagues.sport_id
                LEFT JOIN teams ON teams.id = players.team_id
                WHERE prop_results.external_source = 'api-sports-football'
                  AND prop_results.external_source_identifier LIKE 'api-sports-football:%'
                  AND split_part(prop_results.external_source_identifier, ':', 3) <> ''
                  AND sports.code = 'SOCCER'
                GROUP BY sports.id, split_part(prop_results.external_source_identifier, ':', 3)
                ON CONFLICT DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "provider_entity_links");
        }
    }
}

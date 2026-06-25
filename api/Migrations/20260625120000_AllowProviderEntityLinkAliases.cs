using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SportsBetting.Api.Data;

#nullable disable

namespace SportsBetting.Api.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260625120000_AllowProviderEntityLinkAliases")]
public partial class AllowProviderEntityLinkAliases : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_provider_entity_links_provider_identity_status",
            table: "provider_entity_links");

        migrationBuilder.CreateIndex(
            name: "ix_provider_entity_links_provider_identity_status",
            table: "provider_entity_links",
            columns: new[] { "provider_key", "entity_type", "provider_entity_id", "sport_id", "link_status", "canonical_entity_id" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_provider_entity_links_provider_identity_status",
            table: "provider_entity_links");

        migrationBuilder.CreateIndex(
            name: "ix_provider_entity_links_provider_identity_status",
            table: "provider_entity_links",
            columns: new[] { "provider_key", "entity_type", "provider_entity_id", "sport_id", "link_status" },
            unique: true);
    }
}

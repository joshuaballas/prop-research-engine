using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SportsBetting.Api.Data;

#nullable disable

namespace SportsBetting.Api.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260612124000_AddPlayerExternalRefs")]
    public partial class AddPlayerExternalRefs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "player_external_refs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    provider_player_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    provider_display_name = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_player_external_refs", x => x.id);
                    table.ForeignKey(
                        name: "fk_player_external_refs_players_player_id",
                        column: x => x.player_id,
                        principalTable: "players",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_player_external_refs_player_id",
                table: "player_external_refs",
                column: "player_id");

            migrationBuilder.CreateIndex(
                name: "ix_player_external_refs_provider_provider_display_name",
                table: "player_external_refs",
                columns: new[] { "provider", "provider_display_name" });

            migrationBuilder.CreateIndex(
                name: "ix_player_external_refs_provider_provider_player_id",
                table: "player_external_refs",
                columns: new[] { "provider", "provider_player_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "player_external_refs");
        }
    }
}

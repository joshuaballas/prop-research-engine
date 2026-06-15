using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SportsBetting.Api.Data;

#nullable disable

namespace SportsBetting.Api.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260612150000_AddPlayerMergeAudits")]
    public partial class AddPlayerMergeAudits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "player_merge_audits",
                columns: table => new
                {
                    merge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    survivor_player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    merged_player_id = table.Column<Guid>(type: "uuid", nullable: false),
                    merged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    affected_props = table.Column<int>(type: "integer", nullable: false),
                    affected_snapshots = table.Column<int>(type: "integer", nullable: false),
                    affected_results = table.Column<int>(type: "integer", nullable: false),
                    affected_mappings = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_player_merge_audits", x => x.merge_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_player_merge_audits_merged_at",
                table: "player_merge_audits",
                column: "merged_at");

            migrationBuilder.CreateIndex(
                name: "ix_player_merge_audits_merged_player_id",
                table: "player_merge_audits",
                column: "merged_player_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_player_merge_audits_survivor_player_id",
                table: "player_merge_audits",
                column: "survivor_player_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "player_merge_audits");
        }
    }
}

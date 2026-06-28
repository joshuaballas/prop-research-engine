using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SportsBetting.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRecommendationModelEra : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "model_era",
                table: "recommendation_snapshots",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "model_version",
                table: "recommendation_snapshots",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "probability_model_version",
                table: "recommendation_snapshots",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "recommendation_contract_version",
                table: "recommendation_snapshots",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "model_era",
                table: "market_top_pick_snapshots",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "model_version",
                table: "market_top_pick_snapshots",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "probability_model_version",
                table: "market_top_pick_snapshots",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "recommendation_contract_version",
                table: "market_top_pick_snapshots",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "model_era",
                table: "market_line_shopping_snapshots",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "model_version",
                table: "market_line_shopping_snapshots",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "probability_model_version",
                table: "market_line_shopping_snapshots",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "recommendation_contract_version",
                table: "market_line_shopping_snapshots",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_recommendation_snapshots_model_era",
                table: "recommendation_snapshots",
                column: "model_era");

            migrationBuilder.CreateIndex(
                name: "ix_market_top_pick_snapshots_model_era",
                table: "market_top_pick_snapshots",
                column: "model_era");

            migrationBuilder.CreateIndex(
                name: "ix_market_line_shopping_snapshots_model_era",
                table: "market_line_shopping_snapshots",
                column: "model_era");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_recommendation_snapshots_model_era",
                table: "recommendation_snapshots");

            migrationBuilder.DropIndex(
                name: "ix_market_top_pick_snapshots_model_era",
                table: "market_top_pick_snapshots");

            migrationBuilder.DropIndex(
                name: "ix_market_line_shopping_snapshots_model_era",
                table: "market_line_shopping_snapshots");

            migrationBuilder.DropColumn(
                name: "model_era",
                table: "recommendation_snapshots");

            migrationBuilder.DropColumn(
                name: "model_version",
                table: "recommendation_snapshots");

            migrationBuilder.DropColumn(
                name: "probability_model_version",
                table: "recommendation_snapshots");

            migrationBuilder.DropColumn(
                name: "recommendation_contract_version",
                table: "recommendation_snapshots");

            migrationBuilder.DropColumn(
                name: "model_era",
                table: "market_top_pick_snapshots");

            migrationBuilder.DropColumn(
                name: "model_version",
                table: "market_top_pick_snapshots");

            migrationBuilder.DropColumn(
                name: "probability_model_version",
                table: "market_top_pick_snapshots");

            migrationBuilder.DropColumn(
                name: "recommendation_contract_version",
                table: "market_top_pick_snapshots");

            migrationBuilder.DropColumn(
                name: "model_era",
                table: "market_line_shopping_snapshots");

            migrationBuilder.DropColumn(
                name: "model_version",
                table: "market_line_shopping_snapshots");

            migrationBuilder.DropColumn(
                name: "probability_model_version",
                table: "market_line_shopping_snapshots");

            migrationBuilder.DropColumn(
                name: "recommendation_contract_version",
                table: "market_line_shopping_snapshots");
        }
    }
}

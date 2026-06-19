using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SportsBetting.Api.Data;

#nullable disable

namespace SportsBetting.Api.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260619180000_AddRecommendationPerformanceExclusions")]
    public partial class AddRecommendationPerformanceExclusions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_excluded_from_performance",
                table: "recommendation_snapshots",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "performance_excluded_by",
                table: "recommendation_snapshots",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "performance_excluded_at_utc",
                table: "recommendation_snapshots",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "performance_exclusion_reason",
                table: "recommendation_snapshots",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_recommendation_snapshots_performance_excluded",
                table: "recommendation_snapshots",
                column: "is_excluded_from_performance");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_recommendation_snapshots_performance_excluded",
                table: "recommendation_snapshots");

            migrationBuilder.DropColumn(
                name: "is_excluded_from_performance",
                table: "recommendation_snapshots");

            migrationBuilder.DropColumn(
                name: "performance_excluded_by",
                table: "recommendation_snapshots");

            migrationBuilder.DropColumn(
                name: "performance_excluded_at_utc",
                table: "recommendation_snapshots");

            migrationBuilder.DropColumn(
                name: "performance_exclusion_reason",
                table: "recommendation_snapshots");
        }
    }
}

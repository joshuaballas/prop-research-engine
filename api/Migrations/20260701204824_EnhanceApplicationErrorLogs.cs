using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SportsBetting.Api.Migrations
{
    /// <inheritdoc />
    public partial class EnhanceApplicationErrorLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "http_method",
                table: "application_errors",
                newName: "request_method");

            migrationBuilder.AlterColumn<string>(
                name: "operation",
                table: "application_errors",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "created_at_utc",
                table: "application_errors",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddColumn<string>(
                name: "provider",
                table: "application_errors",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "remote_ip_hash",
                table: "application_errors",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "run_id",
                table: "application_errors",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sport",
                table: "application_errors",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "user_agent",
                table: "application_errors",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_application_errors_run_id",
                table: "application_errors",
                column: "run_id");

            migrationBuilder.CreateIndex(
                name: "ix_application_errors_sport_provider",
                table: "application_errors",
                columns: new[] { "sport", "provider" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_application_errors_run_id",
                table: "application_errors");

            migrationBuilder.DropIndex(
                name: "ix_application_errors_sport_provider",
                table: "application_errors");

            migrationBuilder.DropColumn(
                name: "created_at_utc",
                table: "application_errors");

            migrationBuilder.DropColumn(
                name: "provider",
                table: "application_errors");

            migrationBuilder.DropColumn(
                name: "remote_ip_hash",
                table: "application_errors");

            migrationBuilder.DropColumn(
                name: "run_id",
                table: "application_errors");

            migrationBuilder.DropColumn(
                name: "sport",
                table: "application_errors");

            migrationBuilder.DropColumn(
                name: "user_agent",
                table: "application_errors");

            migrationBuilder.RenameColumn(
                name: "request_method",
                table: "application_errors",
                newName: "http_method");

            migrationBuilder.AlterColumn<string>(
                name: "operation",
                table: "application_errors",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);
        }
    }
}

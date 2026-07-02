using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SportsBetting.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthBillingScaffold : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "app_users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_auth_provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    external_subject_id = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    display_name = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_session_id = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    metadata_json = table.Column<JsonDocument>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_app_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "plans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    summary = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    is_public = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    monthly_price_cents = table.Column<int>(type: "integer", nullable: true),
                    annual_price_cents = table.Column<int>(type: "integer", nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    trial_days = table.Column<int>(type: "integer", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plans", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "checkout_intents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    app_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    billing_interval = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    amount_cents = table.Column<int>(type: "integer", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    external_billing_provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    external_checkout_session_id = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    return_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    metadata_json = table.Column<JsonDocument>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_checkout_intents", x => x.id);
                    table.ForeignKey(
                        name: "fk_checkout_intents_app_users_app_user_id",
                        column: x => x.app_user_id,
                        principalTable: "app_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_checkout_intents_plans_plan_id",
                        column: x => x.plan_id,
                        principalTable: "plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "plan_features",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    feature_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    label = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    value = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    included = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plan_features", x => x.id);
                    table.ForeignKey(
                        name: "fk_plan_features_plans_plan_id",
                        column: x => x.plan_id,
                        principalTable: "plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_plan_states",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    app_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    billing_interval = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    current_period_start_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    current_period_end_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    external_billing_provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    external_customer_id = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    external_subscription_id = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_plan_states", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_plan_states_app_users_app_user_id",
                        column: x => x.app_user_id,
                        principalTable: "app_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_plan_states_plans_plan_id",
                        column: x => x.plan_id,
                        principalTable: "plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.InsertData(
                table: "plans",
                columns: new[] { "id", "annual_price_cents", "created_at_utc", "currency", "description", "is_public", "monthly_price_cents", "name", "slug", "sort_order", "status", "summary", "trial_days", "updated_at_utc" },
                values: new object[,]
                {
                    { new Guid("10000000-0000-0000-0000-000000000001"), 0, new DateTimeOffset(new DateTime(2026, 7, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "USD", "Basic access for trying the prop research workflow before paid billing is connected.", true, 0, "Free", "free", 10, "Active", "Starter prop research access.", null, new DateTimeOffset(new DateTime(2026, 7, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("10000000-0000-0000-0000-000000000002"), 29000, new DateTimeOffset(new DateTime(2026, 7, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "USD", "Expanded access for top picks, line shopping, performance views, and entry review workflows. Payment processing is not connected yet.", true, 2900, "Pro", "pro", 20, "Active", "Full prop research workflow access.", null, new DateTimeOffset(new DateTime(2026, 7, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) }
                });

            migrationBuilder.InsertData(
                table: "plan_features",
                columns: new[] { "id", "description", "feature_key", "included", "label", "plan_id", "sort_order", "value" },
                values: new object[,]
                {
                    { new Guid("20000000-0000-0000-0000-000000000001"), "Top-level market view for current props.", "market_overview", true, "Market overview", new Guid("10000000-0000-0000-0000-000000000001"), 10, "Included" },
                    { new Guid("20000000-0000-0000-0000-000000000002"), "Current provider line context.", "basic_line_context", true, "Basic line context", new Guid("10000000-0000-0000-0000-000000000001"), 20, "Included" },
                    { new Guid("20000000-0000-0000-0000-000000000003"), "Access to curated top opportunities.", "top_picks", true, "Curated Top Picks", new Guid("10000000-0000-0000-0000-000000000002"), 10, "Included" },
                    { new Guid("20000000-0000-0000-0000-000000000004"), "Compare provider lines and prices.", "line_shopping", true, "Line shopping", new Guid("10000000-0000-0000-0000-000000000002"), 20, "Included" },
                    { new Guid("20000000-0000-0000-0000-000000000005"), "Review latest graded model results.", "performance", true, "Model performance", new Guid("10000000-0000-0000-0000-000000000002"), 30, "Included" }
                });

            migrationBuilder.CreateIndex(
                name: "ix_app_users_email",
                table: "app_users",
                column: "email");

            migrationBuilder.CreateIndex(
                name: "ix_app_users_external_auth_provider_external_subject_id",
                table: "app_users",
                columns: new[] { "external_auth_provider", "external_subject_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_app_users_role",
                table: "app_users",
                column: "role");

            migrationBuilder.CreateIndex(
                name: "ix_app_users_status",
                table: "app_users",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_checkout_intents_app_user_id_created_at_utc",
                table: "checkout_intents",
                columns: new[] { "app_user_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_checkout_intents_plan_id",
                table: "checkout_intents",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_checkout_intents_status",
                table: "checkout_intents",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_plan_features_plan_id_feature_key",
                table: "plan_features",
                columns: new[] { "plan_id", "feature_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_plan_features_plan_id_sort_order",
                table: "plan_features",
                columns: new[] { "plan_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_plans_slug",
                table: "plans",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_plans_status_is_public_sort_order",
                table: "plans",
                columns: new[] { "status", "is_public", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_user_plan_states_app_user_id",
                table: "user_plan_states",
                column: "app_user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_plan_states_plan_id",
                table: "user_plan_states",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_plan_states_status",
                table: "user_plan_states",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "checkout_intents");

            migrationBuilder.DropTable(
                name: "plan_features");

            migrationBuilder.DropTable(
                name: "user_plan_states");

            migrationBuilder.DropTable(
                name: "app_users");

            migrationBuilder.DropTable(
                name: "plans");
        }
    }
}

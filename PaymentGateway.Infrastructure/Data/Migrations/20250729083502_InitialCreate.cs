using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace PaymentGateway.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "payment");

            migrationBuilder.CreateTable(
                name: "audit_logs",
                schema: "payment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    entity_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    operation = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    old_values = table.Column<string>(type: "jsonb", nullable: true),
                    new_values = table.Column<string>(type: "jsonb", nullable: true),
                    changes = table.Column<string>(type: "jsonb", nullable: true),
                    user_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    user_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ip_address = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_audit_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "teams",
                schema: "payment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    team_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    notification_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    success_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    fail_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    row_version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_teams", x => x.id);
                    table.UniqueConstraint("a_k_teams_team_slug", x => x.team_slug);
                });

            migrationBuilder.CreateTable(
                name: "payments",
                schema: "payment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    order_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    team_slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "RUB"),
                    status = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    customer_email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    payment_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    authorized_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    confirmed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    bank_order_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    card_mask = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    payment_method = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    row_version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_payments", x => x.id);
                    table.ForeignKey(
                        name: "fk_payments_team_slug",
                        column: x => x.team_slug,
                        principalSchema: "payment",
                        principalTable: "teams",
                        principalColumn: "team_slug",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "payment",
                table: "teams",
                columns: new[] { "id", "created_at", "created_by", "fail_url", "is_active", "notification_url", "password_hash", "success_url", "team_name", "team_slug", "updated_at", "updated_by" },
                values: new object[,]
                {
                    { new Guid("11111111-1111-1111-1111-111111111111"), new DateTime(2025, 7, 29, 8, 35, 2, 170, DateTimeKind.Utc).AddTicks(5360), "SYSTEM", "https://demo.example.com/fail", true, "https://webhook.site/demo-notifications", "d3ad9315b7be5dd53b31a273b3b3aba5defe700808305aa16a3062b76658a791", "https://demo.example.com/success", "Demo Team", "demo-team", new DateTime(2025, 7, 29, 8, 35, 2, 170, DateTimeKind.Utc).AddTicks(5450), "SYSTEM" },
                    { new Guid("22222222-2222-2222-2222-222222222222"), new DateTime(2025, 7, 29, 8, 35, 2, 170, DateTimeKind.Utc).AddTicks(5800), "SYSTEM", null, true, null, "ecd71870d1963316a97e3ac3408c9835ad8cf0f3c1bc703527c30265534f75ae", null, "Test Team", "test-team", new DateTime(2025, 7, 29, 8, 35, 2, 170, DateTimeKind.Utc).AddTicks(5800), "SYSTEM" }
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_created_at",
                schema: "payment",
                table: "audit_logs",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_entity_id",
                schema: "payment",
                table: "audit_logs",
                column: "entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_entity_name",
                schema: "payment",
                table: "audit_logs",
                column: "entity_name");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_entity_name_id",
                schema: "payment",
                table: "audit_logs",
                columns: new[] { "entity_name", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_entity_operation_created",
                schema: "payment",
                table: "audit_logs",
                columns: new[] { "entity_name", "operation", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_operation",
                schema: "payment",
                table: "audit_logs",
                column: "operation");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_user_id",
                schema: "payment",
                table: "audit_logs",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_created_at",
                schema: "payment",
                table: "payments",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_payments_order_id",
                schema: "payment",
                table: "payments",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_payment_id",
                schema: "payment",
                table: "payments",
                column: "payment_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payments_status",
                schema: "payment",
                table: "payments",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_payments_team_slug",
                schema: "payment",
                table: "payments",
                column: "team_slug");

            migrationBuilder.CreateIndex(
                name: "ix_payments_team_slug_order_id",
                schema: "payment",
                table: "payments",
                columns: new[] { "team_slug", "order_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_teams_is_active",
                schema: "payment",
                table: "teams",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_teams_team_name",
                schema: "payment",
                table: "teams",
                column: "team_name");

            migrationBuilder.CreateIndex(
                name: "ix_teams_team_slug",
                schema: "payment",
                table: "teams",
                column: "team_slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_logs",
                schema: "payment");

            migrationBuilder.DropTable(
                name: "payments",
                schema: "payment");

            migrationBuilder.DropTable(
                name: "teams",
                schema: "payment");
        }
    }
}

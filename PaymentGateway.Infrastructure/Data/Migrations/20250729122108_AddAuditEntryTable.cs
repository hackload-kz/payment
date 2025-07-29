using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentGateway.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditEntryTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "f_k_customer_teams_team_id1",
                schema: "payment",
                table: "customer");

            migrationBuilder.DropForeignKey(
                name: "f_k_payment_method_info_customer_customer_id1",
                schema: "payment",
                table: "payment_method_info");

            migrationBuilder.DropForeignKey(
                name: "f_k_payment_method_info_teams_team_id1",
                schema: "payment",
                table: "payment_method_info");

            migrationBuilder.DropForeignKey(
                name: "f_k_payment_payment_method_info_payment_method_info_payment_met~",
                schema: "payment",
                table: "payment_payment_method_info");

            migrationBuilder.DropForeignKey(
                name: "f_k_payments_customer_customer_id1",
                schema: "payment",
                table: "payments");

            migrationBuilder.DropForeignKey(
                name: "f_k_transaction_payments_payment_id1",
                schema: "payment",
                table: "transaction");

            migrationBuilder.DropForeignKey(
                name: "f_k_transaction_transaction_parent_transaction_id1",
                schema: "payment",
                table: "transaction");

            migrationBuilder.DropPrimaryKey(
                name: "p_k_transaction",
                schema: "payment",
                table: "transaction");

            migrationBuilder.DropPrimaryKey(
                name: "p_k_payment_method_info",
                schema: "payment",
                table: "payment_method_info");

            migrationBuilder.DropPrimaryKey(
                name: "p_k_customer",
                schema: "payment",
                table: "customer");

            migrationBuilder.RenameTable(
                name: "transaction",
                schema: "payment",
                newName: "transactions",
                newSchema: "payment");

            migrationBuilder.RenameTable(
                name: "payment_method_info",
                schema: "payment",
                newName: "payment_methods",
                newSchema: "payment");

            migrationBuilder.RenameTable(
                name: "customer",
                schema: "payment",
                newName: "customers",
                newSchema: "payment");

            migrationBuilder.RenameIndex(
                name: "i_x_transaction_payment_id1",
                schema: "payment",
                table: "transactions",
                newName: "i_x_transactions_payment_id1");

            migrationBuilder.RenameIndex(
                name: "i_x_transaction_parent_transaction_id1",
                schema: "payment",
                table: "transactions",
                newName: "i_x_transactions_parent_transaction_id1");

            migrationBuilder.RenameIndex(
                name: "i_x_payment_method_info_team_id1",
                schema: "payment",
                table: "payment_methods",
                newName: "i_x_payment_methods_team_id1");

            migrationBuilder.RenameIndex(
                name: "i_x_payment_method_info_customer_id1",
                schema: "payment",
                table: "payment_methods",
                newName: "i_x_payment_methods_customer_id1");

            migrationBuilder.RenameIndex(
                name: "i_x_customer_team_id1",
                schema: "payment",
                table: "customers",
                newName: "i_x_customers_team_id1");

            migrationBuilder.AddPrimaryKey(
                name: "p_k_transactions",
                schema: "payment",
                table: "transactions",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "p_k_payment_methods",
                schema: "payment",
                table: "payment_methods",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "p_k_customers",
                schema: "payment",
                table: "customers",
                column: "id");

            migrationBuilder.CreateTable(
                name: "audit_entries",
                schema: "payment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    action = table.Column<int>(type: "integer", nullable: false),
                    user_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    team_slug = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    details = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    entity_snapshot_before = table.Column<string>(type: "text", nullable: true),
                    entity_snapshot_after = table.Column<string>(type: "text", nullable: false),
                    correlation_id = table.Column<string>(type: "text", nullable: true),
                    request_id = table.Column<string>(type: "text", nullable: true),
                    ip_address = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    session_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    risk_score = table.Column<decimal>(type: "numeric", nullable: true),
                    severity = table.Column<int>(type: "integer", nullable: false),
                    category = table.Column<int>(type: "integer", nullable: false),
                    metadata = table.Column<string>(type: "text", nullable: true),
                    integrity_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    is_sensitive = table.Column<bool>(type: "boolean", nullable: false),
                    is_archived = table.Column<bool>(type: "boolean", nullable: false),
                    archived_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_audit_entries", x => x.id);
                });

            migrationBuilder.UpdateData(
                schema: "payment",
                table: "teams",
                keyColumn: "id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111111"),
                columns: new[] { "business_info", "created_at", "metadata", "supported_currencies", "updated_at" },
                values: new object[] { new Dictionary<string, string>(), new DateTime(2025, 7, 29, 12, 21, 7, 711, DateTimeKind.Utc).AddTicks(3360), new Dictionary<string, string>(), new List<string> { "RUB" }, new DateTime(2025, 7, 29, 12, 21, 7, 711, DateTimeKind.Utc).AddTicks(3450) });

            migrationBuilder.UpdateData(
                schema: "payment",
                table: "teams",
                keyColumn: "id",
                keyValue: new Guid("22222222-2222-2222-2222-222222222222"),
                columns: new[] { "business_info", "created_at", "metadata", "supported_currencies", "updated_at" },
                values: new object[] { new Dictionary<string, string>(), new DateTime(2025, 7, 29, 12, 21, 7, 711, DateTimeKind.Utc).AddTicks(3880), new Dictionary<string, string>(), new List<string> { "RUB" }, new DateTime(2025, 7, 29, 12, 21, 7, 711, DateTimeKind.Utc).AddTicks(3880) });

            migrationBuilder.AddForeignKey(
                name: "f_k_customers_teams_team_id1",
                schema: "payment",
                table: "customers",
                column: "team_id1",
                principalSchema: "payment",
                principalTable: "teams",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "f_k_payment_methods_customers_customer_id1",
                schema: "payment",
                table: "payment_methods",
                column: "customer_id1",
                principalSchema: "payment",
                principalTable: "customers",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "f_k_payment_methods_teams_team_id1",
                schema: "payment",
                table: "payment_methods",
                column: "team_id1",
                principalSchema: "payment",
                principalTable: "teams",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "f_k_payment_payment_method_info_payment_methods_payment_methods~",
                schema: "payment",
                table: "payment_payment_method_info",
                column: "payment_methods_id",
                principalSchema: "payment",
                principalTable: "payment_methods",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "f_k_payments_customers_customer_id1",
                schema: "payment",
                table: "payments",
                column: "customer_id1",
                principalSchema: "payment",
                principalTable: "customers",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "f_k_transactions_payments_payment_id1",
                schema: "payment",
                table: "transactions",
                column: "payment_id1",
                principalSchema: "payment",
                principalTable: "payments",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "f_k_transactions_transactions_parent_transaction_id1",
                schema: "payment",
                table: "transactions",
                column: "parent_transaction_id1",
                principalSchema: "payment",
                principalTable: "transactions",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "f_k_customers_teams_team_id1",
                schema: "payment",
                table: "customers");

            migrationBuilder.DropForeignKey(
                name: "f_k_payment_methods_customers_customer_id1",
                schema: "payment",
                table: "payment_methods");

            migrationBuilder.DropForeignKey(
                name: "f_k_payment_methods_teams_team_id1",
                schema: "payment",
                table: "payment_methods");

            migrationBuilder.DropForeignKey(
                name: "f_k_payment_payment_method_info_payment_methods_payment_methods~",
                schema: "payment",
                table: "payment_payment_method_info");

            migrationBuilder.DropForeignKey(
                name: "f_k_payments_customers_customer_id1",
                schema: "payment",
                table: "payments");

            migrationBuilder.DropForeignKey(
                name: "f_k_transactions_payments_payment_id1",
                schema: "payment",
                table: "transactions");

            migrationBuilder.DropForeignKey(
                name: "f_k_transactions_transactions_parent_transaction_id1",
                schema: "payment",
                table: "transactions");

            migrationBuilder.DropTable(
                name: "audit_entries",
                schema: "payment");

            migrationBuilder.DropPrimaryKey(
                name: "p_k_transactions",
                schema: "payment",
                table: "transactions");

            migrationBuilder.DropPrimaryKey(
                name: "p_k_payment_methods",
                schema: "payment",
                table: "payment_methods");

            migrationBuilder.DropPrimaryKey(
                name: "p_k_customers",
                schema: "payment",
                table: "customers");

            migrationBuilder.RenameTable(
                name: "transactions",
                schema: "payment",
                newName: "transaction",
                newSchema: "payment");

            migrationBuilder.RenameTable(
                name: "payment_methods",
                schema: "payment",
                newName: "payment_method_info",
                newSchema: "payment");

            migrationBuilder.RenameTable(
                name: "customers",
                schema: "payment",
                newName: "customer",
                newSchema: "payment");

            migrationBuilder.RenameIndex(
                name: "i_x_transactions_payment_id1",
                schema: "payment",
                table: "transaction",
                newName: "i_x_transaction_payment_id1");

            migrationBuilder.RenameIndex(
                name: "i_x_transactions_parent_transaction_id1",
                schema: "payment",
                table: "transaction",
                newName: "i_x_transaction_parent_transaction_id1");

            migrationBuilder.RenameIndex(
                name: "i_x_payment_methods_team_id1",
                schema: "payment",
                table: "payment_method_info",
                newName: "i_x_payment_method_info_team_id1");

            migrationBuilder.RenameIndex(
                name: "i_x_payment_methods_customer_id1",
                schema: "payment",
                table: "payment_method_info",
                newName: "i_x_payment_method_info_customer_id1");

            migrationBuilder.RenameIndex(
                name: "i_x_customers_team_id1",
                schema: "payment",
                table: "customer",
                newName: "i_x_customer_team_id1");

            migrationBuilder.AddPrimaryKey(
                name: "p_k_transaction",
                schema: "payment",
                table: "transaction",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "p_k_payment_method_info",
                schema: "payment",
                table: "payment_method_info",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "p_k_customer",
                schema: "payment",
                table: "customer",
                column: "id");

            migrationBuilder.UpdateData(
                schema: "payment",
                table: "teams",
                keyColumn: "id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111111"),
                columns: new[] { "business_info", "created_at", "metadata", "supported_currencies", "updated_at" },
                values: new object[] { new Dictionary<string, string>(), new DateTime(2025, 7, 29, 12, 19, 55, 36, DateTimeKind.Utc).AddTicks(1630), new Dictionary<string, string>(), new List<string> { "RUB" }, new DateTime(2025, 7, 29, 12, 19, 55, 36, DateTimeKind.Utc).AddTicks(1790) });

            migrationBuilder.UpdateData(
                schema: "payment",
                table: "teams",
                keyColumn: "id",
                keyValue: new Guid("22222222-2222-2222-2222-222222222222"),
                columns: new[] { "business_info", "created_at", "metadata", "supported_currencies", "updated_at" },
                values: new object[] { new Dictionary<string, string>(), new DateTime(2025, 7, 29, 12, 19, 55, 36, DateTimeKind.Utc).AddTicks(2240), new Dictionary<string, string>(), new List<string> { "RUB" }, new DateTime(2025, 7, 29, 12, 19, 55, 36, DateTimeKind.Utc).AddTicks(2240) });

            migrationBuilder.AddForeignKey(
                name: "f_k_customer_teams_team_id1",
                schema: "payment",
                table: "customer",
                column: "team_id1",
                principalSchema: "payment",
                principalTable: "teams",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "f_k_payment_method_info_customer_customer_id1",
                schema: "payment",
                table: "payment_method_info",
                column: "customer_id1",
                principalSchema: "payment",
                principalTable: "customer",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "f_k_payment_method_info_teams_team_id1",
                schema: "payment",
                table: "payment_method_info",
                column: "team_id1",
                principalSchema: "payment",
                principalTable: "teams",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "f_k_payment_payment_method_info_payment_method_info_payment_met~",
                schema: "payment",
                table: "payment_payment_method_info",
                column: "payment_methods_id",
                principalSchema: "payment",
                principalTable: "payment_method_info",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "f_k_payments_customer_customer_id1",
                schema: "payment",
                table: "payments",
                column: "customer_id1",
                principalSchema: "payment",
                principalTable: "customer",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "f_k_transaction_payments_payment_id1",
                schema: "payment",
                table: "transaction",
                column: "payment_id1",
                principalSchema: "payment",
                principalTable: "payments",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "f_k_transaction_transaction_parent_transaction_id1",
                schema: "payment",
                table: "transaction",
                column: "parent_transaction_id1",
                principalSchema: "payment",
                principalTable: "transaction",
                principalColumn: "id");
        }
    }
}

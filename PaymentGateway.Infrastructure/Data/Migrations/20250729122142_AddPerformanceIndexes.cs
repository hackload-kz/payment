using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentGateway.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Performance indexes for Payments table
            migrationBuilder.CreateIndex(
                name: "ix_payments_status_created_at",
                schema: "payment",
                table: "payments",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_team_id_status",
                schema: "payment",
                table: "payments",
                columns: new[] { "team_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_external_reference_id",
                schema: "payment",
                table: "payments",
                column: "external_reference_id")
                .Annotation("Npgsql:IndexMethod", "btree");

            migrationBuilder.CreateIndex(
                name: "ix_payments_amount_currency",
                schema: "payment",
                table: "payments",
                columns: new[] { "amount", "currency" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_created_at_desc",
                schema: "payment",
                table: "payments",
                column: "created_at",
                descending: new[] { true });

            // Performance indexes for Transactions table
            migrationBuilder.CreateIndex(
                name: "ix_transactions_status_created_at",
                schema: "payment",
                table: "transactions",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_transactions_payment_id_status",
                schema: "payment",
                table: "transactions",
                columns: new[] { "payment_id1", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_transactions_external_id",
                schema: "payment",
                table: "transactions",
                column: "external_id",
                unique: true)
                .Annotation("Npgsql:IndexMethod", "btree");

            migrationBuilder.CreateIndex(
                name: "ix_transactions_type_status",
                schema: "payment",
                table: "transactions",
                columns: new[] { "type", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_transactions_created_at_desc",
                schema: "payment",
                table: "transactions",
                column: "created_at",
                descending: new[] { true });

            // Performance indexes for Customers table
            migrationBuilder.CreateIndex(
                name: "ix_customers_email",
                schema: "payment",
                table: "customers",
                column: "email",
                unique: true)
                .Annotation("Npgsql:IndexMethod", "btree");

            migrationBuilder.CreateIndex(
                name: "ix_customers_team_id_email",
                schema: "payment",
                table: "customers",
                columns: new[] { "team_id1", "email" });

            migrationBuilder.CreateIndex(
                name: "ix_customers_phone_number",
                schema: "payment",
                table: "customers",
                column: "phone_number")
                .Annotation("Npgsql:IndexMethod", "btree");

            migrationBuilder.CreateIndex(
                name: "ix_customers_external_id",
                schema: "payment",
                table: "customers",
                column: "external_id",
                unique: true)
                .Annotation("Npgsql:IndexMethod", "btree");

            // Performance indexes for PaymentMethods table
            migrationBuilder.CreateIndex(
                name: "ix_payment_methods_customer_id_is_default",
                schema: "payment",
                table: "payment_methods",
                columns: new[] { "customer_id1", "is_default" });

            migrationBuilder.CreateIndex(
                name: "ix_payment_methods_type_is_active",
                schema: "payment",
                table: "payment_methods",
                columns: new[] { "type", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_payment_methods_token_hash",
                schema: "payment",
                table: "payment_methods",
                column: "token_hash",
                unique: true)
                .Annotation("Npgsql:IndexMethod", "hash");

            // Performance indexes for Teams table
            migrationBuilder.CreateIndex(
                name: "ix_teams_slug",
                schema: "payment",
                table: "teams",
                column: "slug",
                unique: true)
                .Annotation("Npgsql:IndexMethod", "btree");

            migrationBuilder.CreateIndex(
                name: "ix_teams_is_active",
                schema: "payment",
                table: "teams",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_teams_created_at_desc",
                schema: "payment",
                table: "teams",
                column: "created_at",
                descending: new[] { true });

            // Performance indexes for AuditEntries table
            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_entity_id_timestamp",
                schema: "payment",
                table: "audit_entries",
                columns: new[] { "entity_id", "timestamp" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_entity_type_action",
                schema: "payment",
                table: "audit_entries",
                columns: new[] { "entity_type", "action" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_timestamp_desc",
                schema: "payment",
                table: "audit_entries",
                column: "timestamp",
                descending: new[] { true });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_user_id_timestamp",
                schema: "payment",
                table: "audit_entries",
                columns: new[] { "user_id", "timestamp" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_team_slug_timestamp",
                schema: "payment",
                table: "audit_entries",
                columns: new[] { "team_slug", "timestamp" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_severity_category",
                schema: "payment",
                table: "audit_entries",
                columns: new[] { "severity", "category" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_risk_score",
                schema: "payment",
                table: "audit_entries",
                column: "risk_score")
                .Annotation("Npgsql:IndexMethod", "btree");

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_is_sensitive",
                schema: "payment",
                table: "audit_entries",
                column: "is_sensitive");

            // Performance indexes for AuditLogs table
            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_timestamp_desc",
                schema: "payment",
                table: "audit_logs",
                column: "timestamp",
                descending: new[] { true });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_level_timestamp",
                schema: "payment",
                table: "audit_logs",
                columns: new[] { "level", "timestamp" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_category_level",
                schema: "payment",
                table: "audit_logs",
                columns: new[] { "category", "level" });

            // Composite indexes for common query patterns
            migrationBuilder.CreateIndex(
                name: "ix_payments_team_status_created",
                schema: "payment",
                table: "payments",
                columns: new[] { "team_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_transactions_payment_status_created",
                schema: "payment",
                table: "transactions",
                columns: new[] { "payment_id1", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_customers_team_created",
                schema: "payment",
                table: "customers",
                columns: new[] { "team_id1", "created_at" });

            // Partial indexes for active/non-deleted records
            migrationBuilder.Sql(@"
                CREATE INDEX CONCURRENTLY ix_payments_active 
                ON payment.payments (id, created_at) 
                WHERE is_deleted = false;
            ");

            migrationBuilder.Sql(@"
                CREATE INDEX CONCURRENTLY ix_transactions_active 
                ON payment.transactions (id, created_at) 
                WHERE is_deleted = false;
            ");

            migrationBuilder.Sql(@"
                CREATE INDEX CONCURRENTLY ix_customers_active 
                ON payment.customers (id, created_at) 
                WHERE is_deleted = false;
            ");

            migrationBuilder.Sql(@"
                CREATE INDEX CONCURRENTLY ix_payment_methods_active 
                ON payment.payment_methods (id, created_at) 
                WHERE is_deleted = false;
            ");

            // Full-text search indexes for text fields
            migrationBuilder.Sql(@"
                CREATE INDEX CONCURRENTLY ix_payments_description_fts 
                ON payment.payments 
                USING gin(to_tsvector('english', description))
                WHERE description IS NOT NULL;
            ");

            migrationBuilder.Sql(@"
                CREATE INDEX CONCURRENTLY ix_customers_full_name_fts 
                ON payment.customers 
                USING gin(to_tsvector('english', full_name))
                WHERE full_name IS NOT NULL;
            ");

            // JSONB indexes for metadata fields
            migrationBuilder.Sql(@"
                CREATE INDEX CONCURRENTLY ix_payments_metadata_gin 
                ON payment.payments 
                USING gin(metadata)
                WHERE metadata IS NOT NULL;
            ");

            migrationBuilder.Sql(@"
                CREATE INDEX CONCURRENTLY ix_teams_metadata_gin 
                ON payment.teams 
                USING gin(metadata)
                WHERE metadata IS NOT NULL;
            ");

            migrationBuilder.Sql(@"
                CREATE INDEX CONCURRENTLY ix_audit_entries_metadata_gin 
                ON payment.audit_entries 
                USING gin(metadata::jsonb)
                WHERE metadata IS NOT NULL;
            ");

            migrationBuilder.UpdateData(
                schema: "payment",
                table: "teams",
                keyColumn: "id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111111"),
                columns: new[] { "business_info", "created_at", "metadata", "supported_currencies", "updated_at" },
                values: new object[] { new Dictionary<string, string>(), new DateTime(2025, 7, 29, 12, 21, 42, 275, DateTimeKind.Utc).AddTicks(8470), new Dictionary<string, string>(), new List<string> { "RUB" }, new DateTime(2025, 7, 29, 12, 21, 42, 275, DateTimeKind.Utc).AddTicks(8600) });

            migrationBuilder.UpdateData(
                schema: "payment",
                table: "teams",
                keyColumn: "id",
                keyValue: new Guid("22222222-2222-2222-2222-222222222222"),
                columns: new[] { "business_info", "created_at", "metadata", "supported_currencies", "updated_at" },
                values: new object[] { new Dictionary<string, string>(), new DateTime(2025, 7, 29, 12, 21, 42, 275, DateTimeKind.Utc).AddTicks(8990), new Dictionary<string, string>(), new List<string> { "RUB" }, new DateTime(2025, 7, 29, 12, 21, 42, 275, DateTimeKind.Utc).AddTicks(9000) });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop custom indexes (reverse order)
            migrationBuilder.Sql("DROP INDEX CONCURRENTLY IF EXISTS payment.ix_audit_entries_metadata_gin;");
            migrationBuilder.Sql("DROP INDEX CONCURRENTLY IF EXISTS payment.ix_teams_metadata_gin;");
            migrationBuilder.Sql("DROP INDEX CONCURRENTLY IF EXISTS payment.ix_payments_metadata_gin;");
            migrationBuilder.Sql("DROP INDEX CONCURRENTLY IF EXISTS payment.ix_customers_full_name_fts;");
            migrationBuilder.Sql("DROP INDEX CONCURRENTLY IF EXISTS payment.ix_payments_description_fts;");
            migrationBuilder.Sql("DROP INDEX CONCURRENTLY IF EXISTS payment.ix_payment_methods_active;");
            migrationBuilder.Sql("DROP INDEX CONCURRENTLY IF EXISTS payment.ix_customers_active;");
            migrationBuilder.Sql("DROP INDEX CONCURRENTLY IF EXISTS payment.ix_transactions_active;");
            migrationBuilder.Sql("DROP INDEX CONCURRENTLY IF EXISTS payment.ix_payments_active;");

            // Drop composite indexes
            migrationBuilder.DropIndex(
                name: "ix_customers_team_created",
                schema: "payment",
                table: "customers");

            migrationBuilder.DropIndex(
                name: "ix_transactions_payment_status_created",
                schema: "payment",
                table: "transactions");

            migrationBuilder.DropIndex(
                name: "ix_payments_team_status_created",
                schema: "payment",
                table: "payments");

            // Drop AuditLogs indexes
            migrationBuilder.DropIndex(
                name: "ix_audit_logs_category_level",
                schema: "payment",
                table: "audit_logs");

            migrationBuilder.DropIndex(
                name: "ix_audit_logs_level_timestamp",
                schema: "payment",
                table: "audit_logs");

            migrationBuilder.DropIndex(
                name: "ix_audit_logs_timestamp_desc",
                schema: "payment",
                table: "audit_logs");

            // Drop AuditEntries indexes
            migrationBuilder.DropIndex(
                name: "ix_audit_entries_is_sensitive",
                schema: "payment",
                table: "audit_entries");

            migrationBuilder.DropIndex(
                name: "ix_audit_entries_risk_score",
                schema: "payment",
                table: "audit_entries");

            migrationBuilder.DropIndex(
                name: "ix_audit_entries_severity_category",
                schema: "payment",
                table: "audit_entries");

            migrationBuilder.DropIndex(
                name: "ix_audit_entries_team_slug_timestamp",
                schema: "payment",
                table: "audit_entries");

            migrationBuilder.DropIndex(
                name: "ix_audit_entries_user_id_timestamp",
                schema: "payment",
                table: "audit_entries");

            migrationBuilder.DropIndex(
                name: "ix_audit_entries_timestamp_desc",
                schema: "payment",
                table: "audit_entries");

            migrationBuilder.DropIndex(
                name: "ix_audit_entries_entity_type_action",
                schema: "payment",
                table: "audit_entries");

            migrationBuilder.DropIndex(
                name: "ix_audit_entries_entity_id_timestamp",
                schema: "payment",
                table: "audit_entries");

            // Drop Teams indexes
            migrationBuilder.DropIndex(
                name: "ix_teams_created_at_desc",
                schema: "payment",
                table: "teams");

            migrationBuilder.DropIndex(
                name: "ix_teams_is_active",
                schema: "payment",
                table: "teams");

            migrationBuilder.DropIndex(
                name: "ix_teams_slug",
                schema: "payment",
                table: "teams");

            // Drop PaymentMethods indexes
            migrationBuilder.DropIndex(
                name: "ix_payment_methods_token_hash",
                schema: "payment",
                table: "payment_methods");

            migrationBuilder.DropIndex(
                name: "ix_payment_methods_type_is_active",
                schema: "payment",
                table: "payment_methods");

            migrationBuilder.DropIndex(
                name: "ix_payment_methods_customer_id_is_default",
                schema: "payment",
                table: "payment_methods");

            // Drop Customers indexes
            migrationBuilder.DropIndex(
                name: "ix_customers_external_id",
                schema: "payment",
                table: "customers");

            migrationBuilder.DropIndex(
                name: "ix_customers_phone_number",
                schema: "payment",
                table: "customers");

            migrationBuilder.DropIndex(
                name: "ix_customers_team_id_email",
                schema: "payment",
                table: "customers");

            migrationBuilder.DropIndex(
                name: "ix_customers_email",
                schema: "payment",
                table: "customers");

            // Drop Transactions indexes
            migrationBuilder.DropIndex(
                name: "ix_transactions_created_at_desc",
                schema: "payment",
                table: "transactions");

            migrationBuilder.DropIndex(
                name: "ix_transactions_type_status",
                schema: "payment",
                table: "transactions");

            migrationBuilder.DropIndex(
                name: "ix_transactions_external_id",
                schema: "payment",
                table: "transactions");

            migrationBuilder.DropIndex(
                name: "ix_transactions_payment_id_status",
                schema: "payment",
                table: "transactions");

            migrationBuilder.DropIndex(
                name: "ix_transactions_status_created_at",
                schema: "payment",
                table: "transactions");

            // Drop Payments indexes
            migrationBuilder.DropIndex(
                name: "ix_payments_created_at_desc",
                schema: "payment",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "ix_payments_amount_currency",
                schema: "payment",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "ix_payments_external_reference_id",
                schema: "payment",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "ix_payments_team_id_status",
                schema: "payment",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "ix_payments_status_created_at",
                schema: "payment",
                table: "payments");

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
        }
    }
}
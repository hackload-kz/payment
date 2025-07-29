using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentGateway.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddComprehensiveAuditAndEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:hstore", ",,");

            migrationBuilder.AddColumn<string>(
                name: "address",
                schema: "payment",
                table: "teams",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "api_version",
                schema: "payment",
                table: "teams",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Dictionary<string, string>>(
                name: "business_info",
                schema: "payment",
                table: "teams",
                type: "hstore",
                nullable: false);

            migrationBuilder.AddColumn<string>(
                name: "cancel_url",
                schema: "payment",
                table: "teams",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "contact_email",
                schema: "payment",
                table: "teams",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "contact_phone",
                schema: "payment",
                table: "teams",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "country",
                schema: "payment",
                table: "teams",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "daily_payment_limit",
                schema: "payment",
                table: "teams",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "deleted_at",
                schema: "payment",
                table: "teams",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "deleted_by",
                schema: "payment",
                table: "teams",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "description",
                schema: "payment",
                table: "teams",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "enable3_d_secure",
                schema: "payment",
                table: "teams",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "enable_fraud_detection",
                schema: "payment",
                table: "teams",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "enable_partial_refunds",
                schema: "payment",
                table: "teams",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "enable_recurring_payments",
                schema: "payment",
                table: "teams",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "enable_refunds",
                schema: "payment",
                table: "teams",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "enable_reversals",
                schema: "payment",
                table: "teams",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "enable_tokenization",
                schema: "payment",
                table: "teams",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "enable_webhooks",
                schema: "payment",
                table: "teams",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "failed_authentication_attempts",
                schema: "payment",
                table: "teams",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "fee_currency",
                schema: "payment",
                table: "teams",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "fixed_processing_fee",
                schema: "payment",
                table: "teams",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "is_deleted",
                schema: "payment",
                table: "teams",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "last_authentication_ip_address",
                schema: "payment",
                table: "teams",
                type: "character varying(45)",
                maxLength: 45,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "last_password_change_at",
                schema: "payment",
                table: "teams",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "last_successful_authentication_at",
                schema: "payment",
                table: "teams",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "legal_name",
                schema: "payment",
                table: "teams",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "locked_until",
                schema: "payment",
                table: "teams",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "max_fraud_score",
                schema: "payment",
                table: "teams",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "max_payment_amount",
                schema: "payment",
                table: "teams",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<Dictionary<string, string>>(
                name: "metadata",
                schema: "payment",
                table: "teams",
                type: "hstore",
                nullable: false);

            migrationBuilder.AddColumn<decimal>(
                name: "min_payment_amount",
                schema: "payment",
                table: "teams",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "monthly_payment_limit",
                schema: "payment",
                table: "teams",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "processing_fee_percentage",
                schema: "payment",
                table: "teams",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "require_manual_review_for_high_risk",
                schema: "payment",
                table: "teams",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "secret_key",
                schema: "payment",
                table: "teams",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "settlement_account_number",
                schema: "payment",
                table: "teams",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "settlement_bank_code",
                schema: "payment",
                table: "teams",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "settlement_delay_days",
                schema: "payment",
                table: "teams",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<List<string>>(
                name: "supported_currencies",
                schema: "payment",
                table: "teams",
                type: "text[]",
                nullable: false);

            migrationBuilder.AddColumn<int[]>(
                name: "supported_payment_methods",
                schema: "payment",
                table: "teams",
                type: "integer[]",
                nullable: false,
                defaultValue: new int[0]);

            migrationBuilder.AddColumn<string>(
                name: "tax_id",
                schema: "payment",
                table: "teams",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "time_zone",
                schema: "payment",
                table: "teams",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "webhook_retry_attempts",
                schema: "payment",
                table: "teams",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "webhook_secret",
                schema: "payment",
                table: "teams",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "webhook_timeout_seconds",
                schema: "payment",
                table: "teams",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "authorization_attempts",
                schema: "payment",
                table: "payments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "authorizing_started_at",
                schema: "payment",
                table: "payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bank_name",
                schema: "payment",
                table: "payments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "cancelling_started_at",
                schema: "payment",
                table: "payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "card_type",
                schema: "payment",
                table: "payments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "confirming_started_at",
                schema: "payment",
                table: "payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "customer_id",
                schema: "payment",
                table: "payments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "customer_id1",
                schema: "payment",
                table: "payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "customer_ip_address",
                schema: "payment",
                table: "payments",
                type: "character varying(45)",
                maxLength: 45,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "deleted_at",
                schema: "payment",
                table: "payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "deleted_by",
                schema: "payment",
                table: "payments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "expired_at",
                schema: "payment",
                table: "payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "expires_at",
                schema: "payment",
                table: "payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "form_showed_at",
                schema: "payment",
                table: "payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "initialized_at",
                schema: "payment",
                table: "payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_deleted",
                schema: "payment",
                table: "payments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "max_allowed_attempts",
                schema: "payment",
                table: "payments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Dictionary<string, string>>(
                name: "metadata",
                schema: "payment",
                table: "payments",
                type: "hstore",
                nullable: false);

            migrationBuilder.AddColumn<int>(
                name: "refund_count",
                schema: "payment",
                table: "payments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "refunded_amount",
                schema: "payment",
                table: "payments",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "refunded_at",
                schema: "payment",
                table: "payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "refunding_started_at",
                schema: "payment",
                table: "payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "rejected_at",
                schema: "payment",
                table: "payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "reversed_at",
                schema: "payment",
                table: "payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "reversing_started_at",
                schema: "payment",
                table: "payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "session_id",
                schema: "payment",
                table: "payments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "team_id",
                schema: "payment",
                table: "payments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "team_id1",
                schema: "payment",
                table: "payments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "user_agent",
                schema: "payment",
                table: "payments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "customer",
                schema: "payment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    postal_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    date_of_birth = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    preferred_language = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    preferred_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    risk_score = table.Column<int>(type: "integer", nullable: false),
                    risk_level = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_blacklisted = table.Column<bool>(type: "boolean", nullable: false),
                    blacklisted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    blacklist_reason = table.Column<string>(type: "text", nullable: true),
                    last_payment_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    total_payment_count = table.Column<int>(type: "integer", nullable: false),
                    total_payment_amount = table.Column<decimal>(type: "numeric", nullable: false),
                    last_login_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_kyc_verified = table.Column<bool>(type: "boolean", nullable: false),
                    kyc_verified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    kyc_document_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    kyc_document_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    kyc_document_expiry_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    metadata = table.Column<Dictionary<string, string>>(type: "hstore", nullable: false),
                    team_id = table.Column<int>(type: "integer", nullable: false),
                    team_id1 = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    row_version = table.Column<byte[]>(type: "bytea", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_customer", x => x.id);
                    table.ForeignKey(
                        name: "f_k_customer_teams_team_id1",
                        column: x => x.team_id1,
                        principalSchema: "payment",
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "transaction",
                schema: "payment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    transaction_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    payment_id = table.Column<int>(type: "integer", nullable: false),
                    payment_external_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    bank_transaction_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    bank_order_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    authorization_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    processor_transaction_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    acquirer_transaction_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    external_transaction_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    card_mask = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    card_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    card_brand = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    card_bin = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    issuing_bank = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    card_country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    processing_started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    processing_completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    authorized_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    captured_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    refunded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reversed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    failed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    response_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    response_message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    customer_ip_address = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is3_d_secure_used = table.Column<bool>(type: "boolean", nullable: false),
                    three_d_secure_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    three_d_secure_status = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    fraud_score = table.Column<int>(type: "integer", nullable: false),
                    risk_category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    attempt_number = table.Column<int>(type: "integer", nullable: false),
                    max_retry_attempts = table.Column<int>(type: "integer", nullable: false),
                    next_retry_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    processing_fee = table.Column<decimal>(type: "numeric", nullable: false),
                    acquirer_fee = table.Column<decimal>(type: "numeric", nullable: false),
                    network_fee = table.Column<decimal>(type: "numeric", nullable: false),
                    total_fees = table.Column<decimal>(type: "numeric", nullable: false),
                    fee_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    settlement_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    settlement_amount = table.Column<decimal>(type: "numeric", nullable: false),
                    settlement_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    exchange_rate = table.Column<decimal>(type: "numeric", nullable: false),
                    processing_metadata = table.Column<Dictionary<string, string>>(type: "hstore", nullable: false),
                    bank_response_data = table.Column<Dictionary<string, string>>(type: "hstore", nullable: false),
                    acquirer_response_data = table.Column<Dictionary<string, string>>(type: "hstore", nullable: false),
                    parent_transaction_id = table.Column<int>(type: "integer", nullable: true),
                    parent_transaction_id1 = table.Column<Guid>(type: "uuid", nullable: true),
                    payment_id1 = table.Column<Guid>(type: "uuid", nullable: false),
                    additional_data = table.Column<Dictionary<string, string>>(type: "hstore", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    row_version = table.Column<byte[]>(type: "bytea", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_transaction", x => x.id);
                    table.ForeignKey(
                        name: "f_k_transaction_payments_payment_id1",
                        column: x => x.payment_id1,
                        principalSchema: "payment",
                        principalTable: "payments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_transaction_transaction_parent_transaction_id1",
                        column: x => x.parent_transaction_id1,
                        principalSchema: "payment",
                        principalTable: "transaction",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "payment_method_info",
                schema: "payment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_method_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    card_mask = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    card_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    card_brand = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    card_bin = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    card_last4 = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    card_expiry_month = table.Column<int>(type: "integer", nullable: true),
                    card_expiry_year = table.Column<int>(type: "integer", nullable: true),
                    card_holder_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    issuing_bank = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    card_country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    token = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    token_expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    token_provider = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    wallet_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    wallet_provider = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    wallet_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    bank_account_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    bank_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    bank_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    sbp_phone_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    sbp_bank_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    usage_count = table.Column<int>(type: "integer", nullable: false),
                    last_used_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    total_amount_processed = table.Column<decimal>(type: "numeric", nullable: false),
                    successful_transactions = table.Column<int>(type: "integer", nullable: false),
                    failed_transactions = table.Column<int>(type: "integer", nullable: false),
                    is_fraudulent = table.Column<bool>(type: "boolean", nullable: false),
                    flagged_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    fraud_reason = table.Column<string>(type: "text", nullable: true),
                    requires_verification = table.Column<bool>(type: "boolean", nullable: false),
                    is_verified = table.Column<bool>(type: "boolean", nullable: false),
                    verified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    customer_id = table.Column<int>(type: "integer", nullable: true),
                    customer_id1 = table.Column<Guid>(type: "uuid", nullable: true),
                    team_id = table.Column<int>(type: "integer", nullable: false),
                    team_id1 = table.Column<Guid>(type: "uuid", nullable: false),
                    metadata = table.Column<Dictionary<string, string>>(type: "hstore", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    row_version = table.Column<byte[]>(type: "bytea", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_payment_method_info", x => x.id);
                    table.ForeignKey(
                        name: "f_k_payment_method_info_customer_customer_id1",
                        column: x => x.customer_id1,
                        principalSchema: "payment",
                        principalTable: "customer",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "f_k_payment_method_info_teams_team_id1",
                        column: x => x.team_id1,
                        principalSchema: "payment",
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payment_payment_method_info",
                schema: "payment",
                columns: table => new
                {
                    payment_methods_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payments_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_payment_payment_method_info", x => new { x.payment_methods_id, x.payments_id });
                    table.ForeignKey(
                        name: "f_k_payment_payment_method_info_payment_method_info_payment_met~",
                        column: x => x.payment_methods_id,
                        principalSchema: "payment",
                        principalTable: "payment_method_info",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_payment_payment_method_info_payments_payments_id",
                        column: x => x.payments_id,
                        principalSchema: "payment",
                        principalTable: "payments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                schema: "payment",
                table: "teams",
                keyColumn: "id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111111"),
                columns: new[] { "address", "api_version", "business_info", "cancel_url", "contact_email", "contact_phone", "country", "created_at", "daily_payment_limit", "deleted_at", "deleted_by", "description", "enable3_d_secure", "enable_fraud_detection", "enable_partial_refunds", "enable_recurring_payments", "enable_refunds", "enable_reversals", "enable_tokenization", "enable_webhooks", "failed_authentication_attempts", "fee_currency", "fixed_processing_fee", "is_deleted", "last_authentication_ip_address", "last_password_change_at", "last_successful_authentication_at", "legal_name", "locked_until", "max_fraud_score", "max_payment_amount", "metadata", "min_payment_amount", "monthly_payment_limit", "processing_fee_percentage", "require_manual_review_for_high_risk", "secret_key", "settlement_account_number", "settlement_bank_code", "settlement_delay_days", "supported_currencies", "supported_payment_methods", "tax_id", "time_zone", "updated_at", "webhook_retry_attempts", "webhook_secret", "webhook_timeout_seconds" },
                values: new object[] { null, "v1", new Dictionary<string, string>(), null, null, null, null, new DateTime(2025, 7, 29, 12, 19, 55, 36, DateTimeKind.Utc).AddTicks(1630), null, null, null, null, true, true, false, false, true, true, true, true, 0, "RUB", 0m, false, null, null, null, null, null, 75, null, new Dictionary<string, string>(), null, null, 0m, true, null, null, null, 1, new List<string> { "RUB" }, new[] { 0 }, null, "UTC", new DateTime(2025, 7, 29, 12, 19, 55, 36, DateTimeKind.Utc).AddTicks(1790), 3, null, 30 });

            migrationBuilder.UpdateData(
                schema: "payment",
                table: "teams",
                keyColumn: "id",
                keyValue: new Guid("22222222-2222-2222-2222-222222222222"),
                columns: new[] { "address", "api_version", "business_info", "cancel_url", "contact_email", "contact_phone", "country", "created_at", "daily_payment_limit", "deleted_at", "deleted_by", "description", "enable3_d_secure", "enable_fraud_detection", "enable_partial_refunds", "enable_recurring_payments", "enable_refunds", "enable_reversals", "enable_tokenization", "enable_webhooks", "failed_authentication_attempts", "fee_currency", "fixed_processing_fee", "is_deleted", "last_authentication_ip_address", "last_password_change_at", "last_successful_authentication_at", "legal_name", "locked_until", "max_fraud_score", "max_payment_amount", "metadata", "min_payment_amount", "monthly_payment_limit", "processing_fee_percentage", "require_manual_review_for_high_risk", "secret_key", "settlement_account_number", "settlement_bank_code", "settlement_delay_days", "supported_currencies", "supported_payment_methods", "tax_id", "time_zone", "updated_at", "webhook_retry_attempts", "webhook_secret", "webhook_timeout_seconds" },
                values: new object[] { null, "v1", new Dictionary<string, string>(), null, null, null, null, new DateTime(2025, 7, 29, 12, 19, 55, 36, DateTimeKind.Utc).AddTicks(2240), null, null, null, null, true, true, false, false, true, true, true, true, 0, "RUB", 0m, false, null, null, null, null, null, 75, null, new Dictionary<string, string>(), null, null, 0m, true, null, null, null, 1, new List<string> { "RUB" }, new[] { 0 }, null, "UTC", new DateTime(2025, 7, 29, 12, 19, 55, 36, DateTimeKind.Utc).AddTicks(2240), 3, null, 30 });

            migrationBuilder.CreateIndex(
                name: "i_x_payments_customer_id1",
                schema: "payment",
                table: "payments",
                column: "customer_id1");

            migrationBuilder.CreateIndex(
                name: "i_x_payments_team_id1",
                schema: "payment",
                table: "payments",
                column: "team_id1");

            migrationBuilder.CreateIndex(
                name: "i_x_customer_team_id1",
                schema: "payment",
                table: "customer",
                column: "team_id1");

            migrationBuilder.CreateIndex(
                name: "i_x_payment_method_info_customer_id1",
                schema: "payment",
                table: "payment_method_info",
                column: "customer_id1");

            migrationBuilder.CreateIndex(
                name: "i_x_payment_method_info_team_id1",
                schema: "payment",
                table: "payment_method_info",
                column: "team_id1");

            migrationBuilder.CreateIndex(
                name: "i_x_payment_payment_method_info_payments_id",
                schema: "payment",
                table: "payment_payment_method_info",
                column: "payments_id");

            migrationBuilder.CreateIndex(
                name: "i_x_transaction_parent_transaction_id1",
                schema: "payment",
                table: "transaction",
                column: "parent_transaction_id1");

            migrationBuilder.CreateIndex(
                name: "i_x_transaction_payment_id1",
                schema: "payment",
                table: "transaction",
                column: "payment_id1");

            migrationBuilder.AddForeignKey(
                name: "f_k_payments_customer_customer_id1",
                schema: "payment",
                table: "payments",
                column: "customer_id1",
                principalSchema: "payment",
                principalTable: "customer",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "f_k_payments_teams_team_id1",
                schema: "payment",
                table: "payments",
                column: "team_id1",
                principalSchema: "payment",
                principalTable: "teams",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "f_k_payments_customer_customer_id1",
                schema: "payment",
                table: "payments");

            migrationBuilder.DropForeignKey(
                name: "f_k_payments_teams_team_id1",
                schema: "payment",
                table: "payments");

            migrationBuilder.DropTable(
                name: "payment_payment_method_info",
                schema: "payment");

            migrationBuilder.DropTable(
                name: "transaction",
                schema: "payment");

            migrationBuilder.DropTable(
                name: "payment_method_info",
                schema: "payment");

            migrationBuilder.DropTable(
                name: "customer",
                schema: "payment");

            migrationBuilder.DropIndex(
                name: "i_x_payments_customer_id1",
                schema: "payment",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "i_x_payments_team_id1",
                schema: "payment",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "address",
                schema: "payment",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "api_version",
                schema: "payment",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "business_info",
                schema: "payment",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "cancel_url",
                schema: "payment",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "contact_email",
                schema: "payment",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "contact_phone",
                schema: "payment",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "country",
                schema: "payment",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "daily_payment_limit",
                schema: "payment",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                schema: "payment",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "deleted_by",
                schema: "payment",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "description",
                schema: "payment",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "enable3_d_secure",
                schema: "payment",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "enable_fraud_detection",
                schema: "payment",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "enable_partial_refunds",
                schema: "payment",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "enable_recurring_payments",
                schema: "payment",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "enable_refunds",
                schema: "payment",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "enable_reversals",
                schema: "payment",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "enable_tokenization",
                schema: "payment",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "enable_webhooks",
                schema: "payment",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "failed_authentication_attempts",
                schema: "payment",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "fee_currency",
                schema: "payment",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "fixed_processing_fee",
                schema: "payment",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "is_deleted",
                schema: "payment",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "last_authentication_ip_address",
                schema: "payment",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "last_password_change_at",
                schema: "payment",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "last_successful_authentication_at",
                schema: "payment",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "legal_name",
                schema: "payment",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "locked_until",
                schema: "payment",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "max_fraud_score",
                schema: "payment",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "max_payment_amount",
                schema: "payment",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "metadata",
                schema: "payment",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "min_payment_amount",
                schema: "payment",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "monthly_payment_limit",
                schema: "payment",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "processing_fee_percentage",
                schema: "payment",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "require_manual_review_for_high_risk",
                schema: "payment",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "secret_key",
                schema: "payment",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "settlement_account_number",
                schema: "payment",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "settlement_bank_code",
                schema: "payment",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "settlement_delay_days",
                schema: "payment",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "supported_currencies",
                schema: "payment",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "supported_payment_methods",
                schema: "payment",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "tax_id",
                schema: "payment",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "time_zone",
                schema: "payment",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "webhook_retry_attempts",
                schema: "payment",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "webhook_secret",
                schema: "payment",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "webhook_timeout_seconds",
                schema: "payment",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "authorization_attempts",
                schema: "payment",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "authorizing_started_at",
                schema: "payment",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "bank_name",
                schema: "payment",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "cancelling_started_at",
                schema: "payment",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "card_type",
                schema: "payment",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "confirming_started_at",
                schema: "payment",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "customer_id",
                schema: "payment",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "customer_id1",
                schema: "payment",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "customer_ip_address",
                schema: "payment",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                schema: "payment",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "deleted_by",
                schema: "payment",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "expired_at",
                schema: "payment",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "expires_at",
                schema: "payment",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "form_showed_at",
                schema: "payment",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "initialized_at",
                schema: "payment",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "is_deleted",
                schema: "payment",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "max_allowed_attempts",
                schema: "payment",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "metadata",
                schema: "payment",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "refund_count",
                schema: "payment",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "refunded_amount",
                schema: "payment",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "refunded_at",
                schema: "payment",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "refunding_started_at",
                schema: "payment",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "rejected_at",
                schema: "payment",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "reversed_at",
                schema: "payment",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "reversing_started_at",
                schema: "payment",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "session_id",
                schema: "payment",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "team_id",
                schema: "payment",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "team_id1",
                schema: "payment",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "user_agent",
                schema: "payment",
                table: "payments");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:hstore", ",,");

            migrationBuilder.UpdateData(
                schema: "payment",
                table: "teams",
                keyColumn: "id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111111"),
                columns: new[] { "created_at", "updated_at" },
                values: new object[] { new DateTime(2025, 7, 29, 8, 35, 2, 170, DateTimeKind.Utc).AddTicks(5360), new DateTime(2025, 7, 29, 8, 35, 2, 170, DateTimeKind.Utc).AddTicks(5450) });

            migrationBuilder.UpdateData(
                schema: "payment",
                table: "teams",
                keyColumn: "id",
                keyValue: new Guid("22222222-2222-2222-2222-222222222222"),
                columns: new[] { "created_at", "updated_at" },
                values: new object[] { new DateTime(2025, 7, 29, 8, 35, 2, 170, DateTimeKind.Utc).AddTicks(5800), new DateTime(2025, 7, 29, 8, 35, 2, 170, DateTimeKind.Utc).AddTicks(5800) });
        }
    }
}

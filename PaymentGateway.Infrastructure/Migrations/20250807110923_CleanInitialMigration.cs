using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentGateway.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CleanInitialMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "payment");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:hstore", ",,");

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
                    password = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    secret_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    last_password_change_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    failed_authentication_attempts = table.Column<int>(type: "integer", nullable: false),
                    locked_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_successful_authentication_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_authentication_ip_address = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    contact_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    contact_phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    notification_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    success_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    fail_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    cancel_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    min_payment_amount = table.Column<decimal>(type: "numeric", nullable: true),
                    max_payment_amount = table.Column<decimal>(type: "numeric", nullable: true),
                    daily_payment_limit = table.Column<decimal>(type: "numeric", nullable: true),
                    monthly_payment_limit = table.Column<decimal>(type: "numeric", nullable: true),
                    daily_transaction_limit = table.Column<int>(type: "integer", nullable: true),
                    supported_currencies = table.Column<string[]>(type: "text[]", nullable: false),
                    supported_payment_methods = table.Column<string[]>(type: "text[]", nullable: false),
                    can_process_refunds = table.Column<bool>(type: "boolean", nullable: false),
                    legal_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    tax_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    address = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    time_zone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    processing_fee_percentage = table.Column<decimal>(type: "numeric", nullable: false),
                    fixed_processing_fee = table.Column<decimal>(type: "numeric", nullable: false),
                    fee_currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    settlement_delay_days = table.Column<int>(type: "integer", nullable: false),
                    settlement_account_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    settlement_bank_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    enable_fraud_detection = table.Column<bool>(type: "boolean", nullable: false),
                    max_fraud_score = table.Column<int>(type: "integer", nullable: false),
                    require_manual_review_for_high_risk = table.Column<bool>(type: "boolean", nullable: false),
                    enable_refunds = table.Column<bool>(type: "boolean", nullable: false),
                    enable_partial_refunds = table.Column<bool>(type: "boolean", nullable: false),
                    enable_reversals = table.Column<bool>(type: "boolean", nullable: false),
                    enable3_d_secure = table.Column<bool>(type: "boolean", nullable: false),
                    enable_tokenization = table.Column<bool>(type: "boolean", nullable: false),
                    enable_recurring_payments = table.Column<bool>(type: "boolean", nullable: false),
                    api_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    enable_webhooks = table.Column<bool>(type: "boolean", nullable: false),
                    webhook_retry_attempts = table.Column<int>(type: "integer", nullable: false),
                    webhook_timeout_seconds = table.Column<int>(type: "integer", nullable: false),
                    webhook_secret = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    metadata = table.Column<Dictionary<string, string>>(type: "hstore", nullable: false),
                    business_info = table.Column<Dictionary<string, string>>(type: "hstore", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    row_version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_teams", x => x.id);
                    table.UniqueConstraint("a_k_teams_team_slug", x => x.team_slug);
                });

            migrationBuilder.CreateTable(
                name: "customers",
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
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
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
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    row_version = table.Column<byte[]>(type: "bytea", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_customers", x => x.id);
                    table.ForeignKey(
                        name: "f_k_customers_teams_team_id",
                        column: x => x.team_id,
                        principalSchema: "payment",
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payment_methods",
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
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    metadata = table.Column<Dictionary<string, string>>(type: "hstore", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    row_version = table.Column<byte[]>(type: "bytea", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_payment_methods", x => x.id);
                    table.ForeignKey(
                        name: "f_k_payment_methods_customers_customer_id",
                        column: x => x.customer_id,
                        principalSchema: "payment",
                        principalTable: "customers",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "f_k_payment_methods_teams_team_id",
                        column: x => x.team_id,
                        principalSchema: "payment",
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
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
                    success_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    fail_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    initialized_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    form_showed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    one_choose_vision_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    finish_authorize_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    authorizing_started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    authorized_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    confirming_started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    confirmed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    cancelling_started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reversing_started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reversed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    refunding_started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    refunded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    rejected_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    expired_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    error_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    error_message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    receipt = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    bank_order_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    card_mask = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    card_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    bank_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    payment_method = table.Column<int>(type: "integer", nullable: false),
                    authorization_attempts = table.Column<int>(type: "integer", nullable: false),
                    max_allowed_attempts = table.Column<int>(type: "integer", nullable: false),
                    customer_ip_address = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    session_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    refunded_amount = table.Column<decimal>(type: "numeric", nullable: false),
                    refund_count = table.Column<int>(type: "integer", nullable: false),
                    metadata = table.Column<Dictionary<string, string>>(type: "hstore", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    updated_by = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    row_version = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_payments", x => x.id);
                    table.ForeignKey(
                        name: "f_k_payments_customers_customer_id",
                        column: x => x.customer_id,
                        principalSchema: "payment",
                        principalTable: "customers",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "f_k_payments_teams_team_id",
                        column: x => x.team_id,
                        principalSchema: "payment",
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_payments_team_slug",
                        column: x => x.team_slug,
                        principalSchema: "payment",
                        principalTable: "teams",
                        principalColumn: "team_slug",
                        onDelete: ReferentialAction.Restrict);
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
                        name: "f_k_payment_payment_method_info_payment_methods_payment_methods~",
                        column: x => x.payment_methods_id,
                        principalSchema: "payment",
                        principalTable: "payment_methods",
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

            migrationBuilder.CreateTable(
                name: "transactions",
                schema: "payment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    transaction_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    parent_transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    additional_data = table.Column<Dictionary<string, string>>(type: "hstore", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    row_version = table.Column<byte[]>(type: "bytea", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("p_k_transactions", x => x.id);
                    table.ForeignKey(
                        name: "f_k_transactions_payments_payment_id",
                        column: x => x.payment_id,
                        principalSchema: "payment",
                        principalTable: "payments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "f_k_transactions_transactions_parent_transaction_id",
                        column: x => x.parent_transaction_id,
                        principalSchema: "payment",
                        principalTable: "transactions",
                        principalColumn: "id");
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
                name: "i_x_customers_team_id",
                schema: "payment",
                table: "customers",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "i_x_payment_methods_customer_id",
                schema: "payment",
                table: "payment_methods",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "i_x_payment_methods_team_id",
                schema: "payment",
                table: "payment_methods",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "i_x_payment_payment_method_info_payments_id",
                schema: "payment",
                table: "payment_payment_method_info",
                column: "payments_id");

            migrationBuilder.CreateIndex(
                name: "i_x_payments_customer_id",
                schema: "payment",
                table: "payments",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "i_x_payments_team_id",
                schema: "payment",
                table: "payments",
                column: "team_id");

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

            migrationBuilder.CreateIndex(
                name: "i_x_transactions_parent_transaction_id",
                schema: "payment",
                table: "transactions",
                column: "parent_transaction_id");

            migrationBuilder.CreateIndex(
                name: "i_x_transactions_payment_id",
                schema: "payment",
                table: "transactions",
                column: "payment_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_entries",
                schema: "payment");

            migrationBuilder.DropTable(
                name: "audit_logs",
                schema: "payment");

            migrationBuilder.DropTable(
                name: "payment_payment_method_info",
                schema: "payment");

            migrationBuilder.DropTable(
                name: "transactions",
                schema: "payment");

            migrationBuilder.DropTable(
                name: "payment_methods",
                schema: "payment");

            migrationBuilder.DropTable(
                name: "payments",
                schema: "payment");

            migrationBuilder.DropTable(
                name: "customers",
                schema: "payment");

            migrationBuilder.DropTable(
                name: "teams",
                schema: "payment");
        }
    }
}

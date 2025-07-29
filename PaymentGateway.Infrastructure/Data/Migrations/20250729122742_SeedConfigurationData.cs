using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaymentGateway.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class SeedConfigurationData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Insert comprehensive payment gateway configuration data
            
            // 1. Payment Processing Configuration
            migrationBuilder.Sql(@"
                INSERT INTO payment.payments (
                    id, amount, currency, description, status, 
                    created_at, updated_at, is_deleted, deleted_at,
                    team_id, external_reference_id, callback_url,
                    success_url, failure_url, metadata, retry_count,
                    expires_at, payment_method_types, is_test_payment,
                    risk_score, fraud_check_passed, processing_fee_amount,
                    processing_fee_currency, gateway_transaction_id, gateway_response_data,
                    customer_ip_address, customer_user_agent, three_ds_required,
                    three_ds_version, three_ds_challenge_required, authorization_code,
                    reference_number, settlement_date, settlement_batch_id, 
                    chargeback_amount, chargeback_reason, refund_amount,
                    partial_capture_amount, void_reason
                ) VALUES 
                (
                    '33333333-3333-3333-3333-333333333333',
                    1000.00, 'RUB', 'Configuration test payment',
                    1, -- Pending
                    NOW(), NOW(), false, NULL,
                    '11111111-1111-1111-1111-111111111111',
                    'CONFIG_TEST_001', 'https://example.com/callback',
                    'https://example.com/success', 'https://example.com/failure',
                    '{""config_type"": ""test"", ""environment"": ""development""}',
                    0, NOW() + INTERVAL '24 hours',
                    ARRAY['card', 'bank_transfer'], true,
                    0.1, true, 30.00, 'RUB',
                    'gw_test_123456', '{""test"": true}',
                    '127.0.0.1', 'Mozilla/5.0 Test Agent',
                    false, NULL, false, NULL,
                    'REF_CONFIG_001', NULL, NULL,
                    0.00, NULL, 0.00, 0.00, NULL
                );
            ");

            // 2. Customer Configuration Data
            migrationBuilder.Sql(@"
                INSERT INTO payment.customers (
                    id, external_id, email, phone_number, full_name,
                    date_of_birth, address_line1, address_line2, city, state,
                    postal_code, country_code, preferred_language, time_zone,
                    kyc_status, kyc_verified_at, risk_level, tags,
                    marketing_consent, created_at, updated_at, is_deleted, deleted_at,
                    team_id1, last_login_at, failed_login_attempts, is_active, notes
                ) VALUES 
                (
                    '44444444-4444-4444-4444-444444444444',
                    'CONFIG_CUSTOMER_001', 'config@example.com', '+7-000-000-0000',
                    'Configuration Test Customer', '1990-01-01',
                    'Test Address Line 1', 'Test Address Line 2',
                    'Moscow', 'Moscow', '101000', 'RU',
                    'ru', 'Europe/Moscow', 2, NOW(),
                    1, ARRAY['configuration', 'test'],
                    true, NOW(), NOW(), false, NULL,
                    '11111111-1111-1111-1111-111111111111',
                    NOW(), 0, true, 'Configuration test customer'
                );
            ");

            // 3. Payment Method Configuration
            migrationBuilder.Sql(@"
                INSERT INTO payment.payment_methods (
                    id, type, provider, token_hash, masked_details,
                    expiry_date, is_default, is_active, created_at, updated_at,
                    is_deleted, deleted_at, customer_id1, team_id1,
                    last_used_at, usage_count, country_code, billing_address,
                    verification_status, verification_date, fingerprint, metadata
                ) VALUES 
                (
                    '55555555-5555-5555-5555-555555555555',
                    1, -- Card
                    'test_provider',
                    'config_token_hash_001',
                    '**** **** **** 1234',
                    '2027-12-31', true, true,
                    NOW(), NOW(), false, NULL,
                    '44444444-4444-4444-4444-444444444444',
                    '11111111-1111-1111-1111-111111111111',
                    NOW(), 1, 'RU',
                    '{""line1"": ""Test Billing Address"", ""city"": ""Moscow"", ""country"": ""RU""}',
                    2, NOW(),
                    'config_fingerprint_001',
                    '{""config_type"": ""test_card"", ""brand"": ""visa""}'
                );
            ");

            // 4. Transaction Configuration Data
            migrationBuilder.Sql(@"
                INSERT INTO payment.transactions (
                    id, external_id, type, status, amount, currency,
                    description, created_at, updated_at, is_deleted, deleted_at,
                    payment_id1, parent_transaction_id1, gateway_transaction_id,
                    gateway_response_data, processing_fee_amount, processing_fee_currency,
                    exchange_rate, original_amount, original_currency, metadata,
                    retry_count, max_retries, next_retry_at, failure_reason,
                    authorization_code, capture_amount, refund_amount, void_reason,
                    settlement_date, settlement_batch_id, reconciliation_status
                ) VALUES 
                (
                    '66666666-6666-6666-6666-666666666666',
                    'CONFIG_TXN_001', 1, -- Payment
                    1, -- Pending
                    1000.00, 'RUB', 'Configuration test transaction',
                    NOW(), NOW(), false, NULL,
                    '33333333-3333-3333-3333-333333333333',
                    NULL, 'gw_txn_config_001',
                    '{""config"": true, ""test_mode"": true}',
                    30.00, 'RUB', 1.0, 1000.00, 'RUB',
                    '{""transaction_config"": ""test""}',
                    0, 3, NULL, NULL, NULL,
                    0.00, 0.00, NULL, NULL, NULL, 1
                );
            ");

            // 5. Configuration-specific Audit Entries
            migrationBuilder.Sql(@"
                INSERT INTO payment.audit_entries (
                    id, entity_id, entity_type, action, user_id, team_slug,
                    timestamp, details, entity_snapshot_before, entity_snapshot_after,
                    correlation_id, request_id, ip_address, user_agent, session_id,
                    risk_score, severity, category, metadata, integrity_hash,
                    is_sensitive, is_archived, archived_at
                ) VALUES 
                (
                    '77777777-7777-7777-7777-777777777777',
                    '33333333-3333-3333-3333-333333333333',
                    'Payment', 0, -- Create
                    'system', 'team1',
                    NOW(), 'Configuration payment created',
                    NULL, '{""id"": ""33333333-3333-3333-3333-333333333333"", ""amount"": 1000.00}',
                    'config_correlation_001', 'config_request_001',
                    '127.0.0.1', 'Configuration Script/1.0',
                    'config_session_001', 0.0, 1, -- Info
                    1, -- Payment
                    '{""source"": ""configuration_seed"", ""automated"": true}',
                    'config_hash_001', false, false, NULL
                );
            ");

            // 6. System Configuration through metadata updates
            migrationBuilder.UpdateData(
                schema: "payment",
                table: "teams",
                keyColumn: "id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111111"),
                columns: new[] { 
                    "business_info", "created_at", "metadata", "supported_currencies", 
                    "updated_at", "webhook_url", "api_version", "features_enabled",
                    "rate_limit_per_minute", "monthly_volume_limit", "processing_fee_percentage",
                    "settlement_schedule", "risk_threshold", "notification_preferences",
                    "integration_type", "sandbox_mode"
                },
                values: new object[] { 
                    new Dictionary<string, string> 
                    {
                        ["company_name"] = "Test Company Ltd",
                        ["tax_id"] = "1234567890",
                        ["registration_number"] = "REG123456",
                        ["business_type"] = "e_commerce",
                        ["industry"] = "retail"
                    },
                    new DateTime(2025, 7, 29, 12, 27, 42, 163, DateTimeKind.Utc).AddTicks(6470),
                    new Dictionary<string, string>
                    {
                        ["environment"] = "development",
                        ["config_version"] = "1.0.0",
                        ["features"] = "card_payments,bank_transfers,recurring",
                        ["webhook_retries"] = "3",
                        ["session_timeout"] = "1800"
                    },
                    new List<string> { "RUB", "USD", "EUR" },
                    new DateTime(2025, 7, 29, 12, 27, 42, 163, DateTimeKind.Utc).AddTicks(6630),
                    "https://api.example.com/webhooks/team1",
                    "v1.0",
                    new List<string> { "payments", "refunds", "subscriptions", "analytics" },
                    1000, 1000000.00m, 2.5m,
                    1, // Daily
                    0.1m,
                    new Dictionary<string, string>
                    {
                        ["email"] = "admin@example.com",
                        ["sms"] = "+7-000-000-0000",
                        ["webhook"] = "true"
                    },
                    1, // API
                    true
                });

            migrationBuilder.UpdateData(
                schema: "payment",
                table: "teams",
                keyColumn: "id",
                keyValue: new Guid("22222222-2222-2222-2222-222222222222"),
                columns: new[] { 
                    "business_info", "created_at", "metadata", "supported_currencies", 
                    "updated_at", "webhook_url", "api_version", "features_enabled",
                    "rate_limit_per_minute", "monthly_volume_limit", "processing_fee_percentage",
                    "settlement_schedule", "risk_threshold", "notification_preferences",
                    "integration_type", "sandbox_mode"
                },
                values: new object[] { 
                    new Dictionary<string, string> 
                    {
                        ["company_name"] = "Partner Corporation",
                        ["tax_id"] = "0987654321",
                        ["registration_number"] = "REG654321",
                        ["business_type"] = "marketplace",
                        ["industry"] = "fintech"
                    },
                    new DateTime(2025, 7, 29, 12, 27, 42, 163, DateTimeKind.Utc).AddTicks(7270),
                    new Dictionary<string, string>
                    {
                        ["environment"] = "production",
                        ["config_version"] = "1.0.0",
                        ["features"] = "card_payments,bank_transfers,marketplace_splits",
                        ["webhook_retries"] = "5",
                        ["session_timeout"] = "3600"
                    },
                    new List<string> { "RUB", "USD" },
                    new DateTime(2025, 7, 29, 12, 27, 42, 163, DateTimeKind.Utc).AddTicks(7270),
                    "https://api.partner.com/webhooks",
                    "v1.0",
                    new List<string> { "payments", "refunds", "marketplace", "analytics", "reporting" },
                    500, 5000000.00m, 1.8m,
                    2, // Weekly
                    0.05m,
                    new Dictionary<string, string>
                    {
                        ["email"] = "notifications@partner.com",
                        ["webhook"] = "true",
                        ["slack"] = "https://hooks.slack.com/services/..."
                    },
                    2, // SDK
                    false
                });

            // 7. Create configuration tables for system settings
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS payment.system_configurations (
                    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
                    key varchar(100) NOT NULL UNIQUE,
                    value text NOT NULL,
                    value_type varchar(20) NOT NULL DEFAULT 'string',
                    description text,
                    is_encrypted boolean NOT NULL DEFAULT false,
                    is_system boolean NOT NULL DEFAULT false,
                    created_at timestamp with time zone NOT NULL DEFAULT NOW(),
                    updated_at timestamp with time zone NOT NULL DEFAULT NOW(),
                    created_by varchar(100),
                    updated_by varchar(100)
                );
            ");

            // 8. Insert system configuration values
            migrationBuilder.Sql(@"
                INSERT INTO payment.system_configurations (key, value, value_type, description, is_system) VALUES
                ('payment.processing.timeout_seconds', '300', 'integer', 'Payment processing timeout in seconds', true),
                ('payment.retry.max_attempts', '3', 'integer', 'Maximum number of retry attempts for failed payments', true),
                ('payment.retry.backoff_multiplier', '2.0', 'decimal', 'Backoff multiplier for retry attempts', true),
                ('security.encryption.algorithm', 'AES-256-GCM', 'string', 'Default encryption algorithm for sensitive data', true),
                ('security.token.expiry_minutes', '60', 'integer', 'Default token expiry time in minutes', true),
                ('audit.retention.days', '2555', 'integer', 'Audit log retention period in days (7 years)', true),
                ('webhook.retry.max_attempts', '5', 'integer', 'Maximum webhook retry attempts', true),
                ('webhook.timeout.seconds', '30', 'integer', 'Webhook request timeout in seconds', true),
                ('rate_limiting.default_per_minute', '100', 'integer', 'Default rate limit per minute', true),
                ('rate_limiting.burst_allowance', '20', 'integer', 'Burst allowance for rate limiting', true),
                ('fraud.risk_threshold.low', '0.3', 'decimal', 'Low risk threshold for fraud detection', true),
                ('fraud.risk_threshold.medium', '0.6', 'decimal', 'Medium risk threshold for fraud detection', true),
                ('fraud.risk_threshold.high', '0.8', 'decimal', 'High risk threshold for fraud detection', true),
                ('notification.email.from', 'noreply@paymentgateway.com', 'string', 'Default sender email address', false),
                ('notification.sms.provider', 'twilio', 'string', 'SMS notification provider', false),
                ('monitoring.health_check.interval_seconds', '30', 'integer', 'Health check interval in seconds', true),
                ('database.connection_pool.max_size', '20', 'integer', 'Maximum database connection pool size', true),
                ('database.query.timeout_seconds', '30', 'integer', 'Database query timeout in seconds', true),
                ('cache.redis.ttl_seconds', '3600', 'integer', 'Default Redis cache TTL in seconds', true),
                ('api.version.current', 'v1.0', 'string', 'Current API version', false),
                ('api.deprecation.notice_days', '90', 'integer', 'API deprecation notice period in days', true);
            ");

            // 9. Create payment method configuration table
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS payment.payment_method_configurations (
                    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
                    method_type varchar(50) NOT NULL,
                    provider varchar(100) NOT NULL,
                    is_enabled boolean NOT NULL DEFAULT true,
                    configuration jsonb NOT NULL,
                    processing_fee_percentage decimal(5,4) NOT NULL DEFAULT 0.0,
                    processing_fee_fixed decimal(10,2) NOT NULL DEFAULT 0.0,
                    currency varchar(3) NOT NULL DEFAULT 'RUB',
                    min_amount decimal(10,2) NOT NULL DEFAULT 0.01,
                    max_amount decimal(10,2) NOT NULL DEFAULT 999999.99,
                    supported_countries text[] NOT NULL DEFAULT ARRAY['RU'],
                    created_at timestamp with time zone NOT NULL DEFAULT NOW(),
                    updated_at timestamp with time zone NOT NULL DEFAULT NOW(),
                    UNIQUE(method_type, provider, currency)
                );
            ");

            // 10. Insert payment method configurations
            migrationBuilder.Sql(@"
                INSERT INTO payment.payment_method_configurations 
                (method_type, provider, configuration, processing_fee_percentage, processing_fee_fixed, min_amount, max_amount) VALUES
                ('card', 'visa', '{""3ds_required"": false, ""cvv_required"": true}', 0.025, 5.00, 1.00, 500000.00),
                ('card', 'mastercard', '{""3ds_required"": false, ""cvv_required"": true}', 0.025, 5.00, 1.00, 500000.00),
                ('card', 'mir', '{""3ds_required"": true, ""cvv_required"": true}', 0.020, 3.00, 1.00, 300000.00),
                ('bank_transfer', 'sberbank', '{""confirmation_required"": true}', 0.015, 10.00, 100.00, 1000000.00),
                ('bank_transfer', 'vtb', '{""confirmation_required"": true}', 0.015, 10.00, 100.00, 1000000.00),
                ('digital_wallet', 'qiwi', '{""phone_required"": true}', 0.030, 2.00, 10.00, 50000.00),
                ('digital_wallet', 'yandex_money', '{""account_required"": true}', 0.030, 2.00, 10.00, 100000.00);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove configuration data (reverse order)
            migrationBuilder.Sql("DROP TABLE IF EXISTS payment.payment_method_configurations;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS payment.system_configurations;");
            
            migrationBuilder.Sql("DELETE FROM payment.audit_entries WHERE id = '77777777-7777-7777-7777-777777777777';");
            migrationBuilder.Sql("DELETE FROM payment.transactions WHERE id = '66666666-6666-6666-6666-666666666666';");
            migrationBuilder.Sql("DELETE FROM payment.payment_methods WHERE id = '55555555-5555-5555-5555-555555555555';");
            migrationBuilder.Sql("DELETE FROM payment.customers WHERE id = '44444444-4444-4444-4444-444444444444';");
            migrationBuilder.Sql("DELETE FROM payment.payments WHERE id = '33333333-3333-3333-3333-333333333333';");

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
    }
}
-- =============================================
-- PaymentGateway Migration Rollback Scripts
-- =============================================
-- This file contains safe rollback procedures for database migrations
-- Use these scripts to safely revert migrations when needed

-- =============================================
-- ROLLBACK: SeedConfigurationData (20250729122742)
-- =============================================

-- Drop configuration tables (in reverse dependency order)
DROP TABLE IF EXISTS payment.payment_method_configurations CASCADE;
DROP TABLE IF EXISTS payment.system_configurations CASCADE;

-- Remove seed data
DELETE FROM payment.audit_entries 
WHERE id = '77777777-7777-7777-7777-777777777777';

DELETE FROM payment.transactions 
WHERE id = '66666666-6666-6666-6666-666666666666';

DELETE FROM payment.payment_methods 
WHERE id = '55555555-5555-5555-5555-555555555555';

DELETE FROM payment.customers 
WHERE id = '44444444-4444-4444-4444-444444444444';

DELETE FROM payment.payments 
WHERE id = '33333333-3333-3333-3333-333333333333';

-- Reset teams to basic configuration
UPDATE payment.teams 
SET 
    business_info = '{}',
    metadata = '{}',
    supported_currencies = ARRAY['RUB'],
    webhook_url = NULL,
    api_version = 'v1.0',
    features_enabled = ARRAY[]::text[],
    rate_limit_per_minute = 100,
    monthly_volume_limit = 0,
    processing_fee_percentage = 0,
    settlement_schedule = 1,
    risk_threshold = 0.5,
    notification_preferences = '{}',
    integration_type = 1,
    sandbox_mode = true
WHERE id IN (
    '11111111-1111-1111-1111-111111111111',
    '22222222-2222-2222-2222-222222222222'
);

-- =============================================
-- ROLLBACK: AddPerformanceIndexes (20250729122142)
-- =============================================

-- Drop custom PostgreSQL indexes
DROP INDEX CONCURRENTLY IF EXISTS payment.ix_audit_entries_metadata_gin;
DROP INDEX CONCURRENTLY IF EXISTS payment.ix_teams_metadata_gin;
DROP INDEX CONCURRENTLY IF EXISTS payment.ix_payments_metadata_gin;
DROP INDEX CONCURRENTLY IF EXISTS payment.ix_customers_full_name_fts;
DROP INDEX CONCURRENTLY IF EXISTS payment.ix_payments_description_fts;
DROP INDEX CONCURRENTLY IF EXISTS payment.ix_payment_methods_active;
DROP INDEX CONCURRENTLY IF EXISTS payment.ix_customers_active;
DROP INDEX CONCURRENTLY IF EXISTS payment.ix_transactions_active;
DROP INDEX CONCURRENTLY IF EXISTS payment.ix_payments_active;

-- Drop composite indexes
DROP INDEX IF EXISTS payment.ix_customers_team_created;
DROP INDEX IF EXISTS payment.ix_transactions_payment_status_created;
DROP INDEX IF EXISTS payment.ix_payments_team_status_created;

-- Drop AuditLogs indexes
DROP INDEX IF EXISTS payment.ix_audit_logs_category_level;
DROP INDEX IF EXISTS payment.ix_audit_logs_level_timestamp;
DROP INDEX IF EXISTS payment.ix_audit_logs_timestamp_desc;

-- Drop AuditEntries indexes
DROP INDEX IF EXISTS payment.ix_audit_entries_is_sensitive;
DROP INDEX IF EXISTS payment.ix_audit_entries_risk_score;
DROP INDEX IF EXISTS payment.ix_audit_entries_severity_category;
DROP INDEX IF EXISTS payment.ix_audit_entries_team_slug_timestamp;
DROP INDEX IF EXISTS payment.ix_audit_entries_user_id_timestamp;
DROP INDEX IF EXISTS payment.ix_audit_entries_timestamp_desc;
DROP INDEX IF EXISTS payment.ix_audit_entries_entity_type_action;
DROP INDEX IF EXISTS payment.ix_audit_entries_entity_id_timestamp;

-- Drop Teams indexes
DROP INDEX IF EXISTS payment.ix_teams_created_at_desc;
DROP INDEX IF EXISTS payment.ix_teams_is_active;
DROP INDEX IF EXISTS payment.ix_teams_slug;

-- Drop PaymentMethods indexes
DROP INDEX IF EXISTS payment.ix_payment_methods_token_hash;
DROP INDEX IF EXISTS payment.ix_payment_methods_type_is_active;
DROP INDEX IF EXISTS payment.ix_payment_methods_customer_id_is_default;

-- Drop Customers indexes
DROP INDEX IF EXISTS payment.ix_customers_external_id;
DROP INDEX IF EXISTS payment.ix_customers_phone_number;
DROP INDEX IF EXISTS payment.ix_customers_team_id_email;
DROP INDEX IF EXISTS payment.ix_customers_email;

-- Drop Transactions indexes
DROP INDEX IF EXISTS payment.ix_transactions_created_at_desc;
DROP INDEX IF EXISTS payment.ix_transactions_type_status;
DROP INDEX IF EXISTS payment.ix_transactions_external_id;
DROP INDEX IF EXISTS payment.ix_transactions_payment_id_status;
DROP INDEX IF EXISTS payment.ix_transactions_status_created_at;

-- Drop Payments indexes
DROP INDEX IF EXISTS payment.ix_payments_created_at_desc;
DROP INDEX IF EXISTS payment.ix_payments_amount_currency;
DROP INDEX IF EXISTS payment.ix_payments_external_reference_id;
DROP INDEX IF EXISTS payment.ix_payments_team_id_status;
DROP INDEX IF EXISTS payment.ix_payments_status_created_at;

-- =============================================
-- ROLLBACK: AddAuditEntryTable (20250729122108)
-- =============================================

-- Drop foreign keys
ALTER TABLE payment.customers DROP CONSTRAINT IF EXISTS f_k_customers_teams_team_id1;
ALTER TABLE payment.payment_methods DROP CONSTRAINT IF EXISTS f_k_payment_methods_customers_customer_id1;
ALTER TABLE payment.payment_methods DROP CONSTRAINT IF EXISTS f_k_payment_methods_teams_team_id1;
ALTER TABLE payment.payment_payment_method_info DROP CONSTRAINT IF EXISTS f_k_payment_payment_method_info_payment_methods_payment_methods;
ALTER TABLE payment.payments DROP CONSTRAINT IF EXISTS f_k_payments_customers_customer_id1;
ALTER TABLE payment.transactions DROP CONSTRAINT IF EXISTS f_k_transactions_payments_payment_id1;
ALTER TABLE payment.transactions DROP CONSTRAINT IF EXISTS f_k_transactions_transactions_parent_transaction_id1;

-- Drop audit entries table
DROP TABLE IF EXISTS payment.audit_entries CASCADE;

-- Rename tables back to singular form
ALTER TABLE payment.transactions RENAME TO transaction;
ALTER TABLE payment.payment_methods RENAME TO payment_method_info;
ALTER TABLE payment.customers RENAME TO customer;

-- Rename indexes back
ALTER INDEX payment.ix_transactions_payment_id1 RENAME TO ix_transaction_payment_id1;
ALTER INDEX payment.ix_transactions_parent_transaction_id1 RENAME TO ix_transaction_parent_transaction_id1;
ALTER INDEX payment.ix_payment_methods_team_id1 RENAME TO ix_payment_method_info_team_id1;
ALTER INDEX payment.ix_payment_methods_customer_id1 RENAME TO ix_payment_method_info_customer_id1;
ALTER INDEX payment.ix_customers_team_id1 RENAME TO ix_customer_team_id1;

-- Recreate primary keys with old names
ALTER TABLE payment.transaction DROP CONSTRAINT IF EXISTS p_k_transactions;
ALTER TABLE payment.transaction ADD CONSTRAINT p_k_transaction PRIMARY KEY (id);

ALTER TABLE payment.payment_method_info DROP CONSTRAINT IF EXISTS p_k_payment_methods;
ALTER TABLE payment.payment_method_info ADD CONSTRAINT p_k_payment_method_info PRIMARY KEY (id);

ALTER TABLE payment.customer DROP CONSTRAINT IF EXISTS p_k_customers;
ALTER TABLE payment.customer ADD CONSTRAINT p_k_customer PRIMARY KEY (id);

-- Recreate foreign keys with old names
ALTER TABLE payment.customer 
ADD CONSTRAINT f_k_customer_teams_team_id1 
FOREIGN KEY (team_id1) REFERENCES payment.teams(id) ON DELETE CASCADE;

ALTER TABLE payment.payment_method_info 
ADD CONSTRAINT f_k_payment_method_info_customer_customer_id1 
FOREIGN KEY (customer_id1) REFERENCES payment.customer(id);

ALTER TABLE payment.payment_method_info 
ADD CONSTRAINT f_k_payment_method_info_teams_team_id1 
FOREIGN KEY (team_id1) REFERENCES payment.teams(id) ON DELETE CASCADE;

ALTER TABLE payment.payment_payment_method_info 
ADD CONSTRAINT f_k_payment_payment_method_info_payment_method_info_payment_met 
FOREIGN KEY (payment_methods_id) REFERENCES payment.payment_method_info(id) ON DELETE CASCADE;

ALTER TABLE payment.payments 
ADD CONSTRAINT f_k_payments_customer_customer_id1 
FOREIGN KEY (customer_id1) REFERENCES payment.customer(id);

ALTER TABLE payment.transaction 
ADD CONSTRAINT f_k_transaction_payments_payment_id1 
FOREIGN KEY (payment_id1) REFERENCES payment.payments(id) ON DELETE CASCADE;

ALTER TABLE payment.transaction 
ADD CONSTRAINT f_k_transaction_transaction_parent_transaction_id1 
FOREIGN KEY (parent_transaction_id1) REFERENCES payment.transaction(id);

-- =============================================
-- ROLLBACK: AddComprehensiveAuditAndEntities (20250729121955)
-- =============================================

-- This rollback is complex due to the extensive changes. 
-- It's recommended to use EF migrations rollback command instead:
-- dotnet ef database update 20250729083502_InitialCreate --project PaymentGateway.Infrastructure --startup-project PaymentGateway.API

-- However, if manual rollback is needed:

-- Drop many-to-many junction table
DROP TABLE IF EXISTS payment.payment_payment_method_info CASCADE;

-- Drop new entity tables in dependency order
DROP TABLE IF EXISTS payment.customer CASCADE;
DROP TABLE IF EXISTS payment.payment_method_info CASCADE;
DROP TABLE IF EXISTS payment.transaction CASCADE;

-- Remove added columns from existing tables (example for payments table)
-- Note: This is dangerous and may cause data loss
/*
ALTER TABLE payment.payments 
DROP COLUMN IF EXISTS external_reference_id,
DROP COLUMN IF EXISTS callback_url,
DROP COLUMN IF EXISTS success_url,
DROP COLUMN IF EXISTS failure_url,
DROP COLUMN IF EXISTS retry_count,
DROP COLUMN IF EXISTS expires_at,
-- ... (add all columns that were added)
;
*/

-- For safety, it's better to restore from backup or use EF migrations rollback

-- =============================================
-- ROLLBACK: InitialCreate (20250729083502)
-- =============================================

-- WARNING: This will destroy all data!
-- Drop all tables and schema
DROP SCHEMA IF EXISTS payment CASCADE;

-- =============================================
-- Utility Functions for Safe Rollback
-- =============================================

-- Create a function to backup data before rollback
CREATE OR REPLACE FUNCTION payment.backup_data_before_rollback(backup_suffix TEXT DEFAULT NULL)
RETURNS TEXT AS $$
DECLARE
    backup_name TEXT;
    table_name TEXT;
    backup_count INTEGER := 0;
BEGIN
    IF backup_suffix IS NULL THEN
        backup_suffix := to_char(NOW(), 'YYYYMMDD_HH24MISS');
    END IF;
    
    backup_name := 'rollback_backup_' || backup_suffix;
    
    -- Create backup schema
    EXECUTE 'CREATE SCHEMA IF NOT EXISTS ' || backup_name;
    
    -- Backup all tables
    FOR table_name IN 
        SELECT tablename 
        FROM pg_tables 
        WHERE schemaname = 'payment'
        ORDER BY tablename
    LOOP
        EXECUTE format('CREATE TABLE %I.%I AS SELECT * FROM payment.%I', 
                      backup_name, table_name, table_name);
        backup_count := backup_count + 1;
    END LOOP;
    
    RETURN format('Backed up %s tables to schema %s', backup_count, backup_name);
END;
$$ LANGUAGE plpgsql;

-- Create a function to verify rollback safety
CREATE OR REPLACE FUNCTION payment.verify_rollback_safety(target_migration TEXT)
RETURNS TABLE(
    check_name TEXT,
    status TEXT,
    details TEXT
) AS $$
BEGIN
    -- Check for foreign key dependencies
    RETURN QUERY
    SELECT 
        'Foreign Key Dependencies'::TEXT,
        CASE WHEN COUNT(*) > 0 THEN 'WARNING' ELSE 'OK' END::TEXT,
        format('Found %s foreign key constraints that may be affected', COUNT(*))::TEXT
    FROM information_schema.table_constraints tc
    WHERE tc.constraint_schema = 'payment' 
    AND tc.constraint_type = 'FOREIGN KEY';
    
    -- Check for data in tables that will be dropped
    RETURN QUERY
    SELECT 
        'Data Loss Risk'::TEXT,
        CASE WHEN (
            SELECT COUNT(*) FROM payment.payments 
            UNION ALL SELECT COUNT(*) FROM payment.teams
        ) > 2 THEN 'WARNING' ELSE 'OK' END::TEXT,
        'Tables contain data that will be lost'::TEXT;
    
    -- Check migration history
    RETURN QUERY
    SELECT 
        'Migration History'::TEXT,
        'INFO'::TEXT,
        format('Current migration: %s, Target: %s', 
               (SELECT migration_id FROM __ef_migrations_history ORDER BY migration_id DESC LIMIT 1),
               target_migration)::TEXT;
END;
$$ LANGUAGE plpgsql;

-- Usage examples:
-- SELECT payment.backup_data_before_rollback();
-- SELECT * FROM payment.verify_rollback_safety('20250729083502_InitialCreate');
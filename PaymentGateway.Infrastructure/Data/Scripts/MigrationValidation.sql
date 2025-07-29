-- =============================================
-- PaymentGateway Migration Validation Scripts
-- =============================================
-- This file contains comprehensive validation tests for database migrations
-- Run these scripts after applying migrations to ensure database integrity

-- =============================================
-- Schema Validation
-- =============================================

-- Verify schema exists
DO $$ 
BEGIN 
    IF NOT EXISTS (SELECT 1 FROM information_schema.schemata WHERE schema_name = 'payment') THEN
        RAISE EXCEPTION 'Payment schema does not exist';
    ELSE
        RAISE NOTICE 'Schema validation: PASSED - payment schema exists';
    END IF;
END $$;

-- =============================================
-- Table Structure Validation
-- =============================================

-- Function to validate table exists with expected columns
CREATE OR REPLACE FUNCTION payment.validate_table_structure(
    table_name TEXT,
    expected_columns TEXT[]
) RETURNS TABLE(
    validation_result TEXT,
    status TEXT,
    details TEXT
) AS $$
DECLARE
    actual_columns TEXT[];
    missing_columns TEXT[];
    extra_columns TEXT[];
BEGIN
    -- Get actual columns
    SELECT array_agg(column_name ORDER BY ordinal_position)
    INTO actual_columns
    FROM information_schema.columns
    WHERE table_schema = 'payment' AND table_name = validate_table_structure.table_name;
    
    IF actual_columns IS NULL THEN
        RETURN QUERY SELECT 
            format('Table %s', table_name)::TEXT,
            'FAILED'::TEXT,
            'Table does not exist'::TEXT;
        RETURN;
    END IF;
    
    -- Check for missing columns
    SELECT array_agg(col)
    INTO missing_columns
    FROM unnest(expected_columns) col
    WHERE col != ALL(actual_columns);
    
    -- Check for extra columns (optional - might be expected)
    SELECT array_agg(col)
    INTO extra_columns
    FROM unnest(actual_columns) col
    WHERE col != ALL(expected_columns);
    
    IF missing_columns IS NOT NULL THEN
        RETURN QUERY SELECT 
            format('Table %s', table_name)::TEXT,
            'FAILED'::TEXT,
            format('Missing columns: %s', array_to_string(missing_columns, ', '))::TEXT;
    ELSEIF extra_columns IS NOT NULL THEN
        RETURN QUERY SELECT 
            format('Table %s', table_name)::TEXT,
            'WARNING'::TEXT,
            format('Extra columns found: %s', array_to_string(extra_columns, ', '))::TEXT;
    ELSE
        RETURN QUERY SELECT 
            format('Table %s', table_name)::TEXT,
            'PASSED'::TEXT,
            'All expected columns present'::TEXT;
    END IF;
END;
$$ LANGUAGE plpgsql;

-- Validate core tables
SELECT * FROM payment.validate_table_structure('teams', ARRAY[
    'id', 'name', 'slug', 'description', 'is_active', 'created_at', 'updated_at',
    'is_deleted', 'deleted_at', 'api_key', 'webhook_secret', 'webhook_url',
    'api_version', 'features_enabled', 'rate_limit_per_minute', 'monthly_volume_limit',
    'processing_fee_percentage', 'settlement_schedule', 'risk_threshold',
    'business_info', 'supported_currencies', 'notification_preferences',
    'integration_type', 'sandbox_mode', 'metadata'
]);

SELECT * FROM payment.validate_table_structure('payments', ARRAY[
    'id', 'amount', 'currency', 'description', 'status', 'created_at', 'updated_at',
    'is_deleted', 'deleted_at', 'team_id', 'external_reference_id', 'callback_url',
    'success_url', 'failure_url', 'metadata', 'retry_count', 'expires_at',
    'payment_method_types', 'is_test_payment', 'risk_score', 'fraud_check_passed'
]);

SELECT * FROM payment.validate_table_structure('customers', ARRAY[
    'id', 'external_id', 'email', 'phone_number', 'full_name', 'date_of_birth',
    'address_line1', 'address_line2', 'city', 'state', 'postal_code', 'country_code',
    'preferred_language', 'time_zone', 'kyc_status', 'kyc_verified_at', 'risk_level',
    'tags', 'marketing_consent', 'created_at', 'updated_at', 'is_deleted', 'deleted_at'
]);

SELECT * FROM payment.validate_table_structure('transactions', ARRAY[
    'id', 'external_id', 'type', 'status', 'amount', 'currency', 'description',
    'created_at', 'updated_at', 'is_deleted', 'deleted_at', 'payment_id1',
    'parent_transaction_id1', 'gateway_transaction_id', 'gateway_response_data'
]);

SELECT * FROM payment.validate_table_structure('payment_methods', ARRAY[
    'id', 'type', 'provider', 'token_hash', 'masked_details', 'expiry_date',
    'is_default', 'is_active', 'created_at', 'updated_at', 'is_deleted', 'deleted_at',
    'customer_id1', 'team_id1'
]);

SELECT * FROM payment.validate_table_structure('audit_entries', ARRAY[
    'id', 'entity_id', 'entity_type', 'action', 'user_id', 'team_slug', 'timestamp',
    'details', 'entity_snapshot_before', 'entity_snapshot_after', 'correlation_id',
    'request_id', 'ip_address', 'user_agent', 'session_id', 'risk_score', 'severity',
    'category', 'metadata', 'integrity_hash', 'is_sensitive', 'is_archived', 'archived_at'
]);

SELECT * FROM payment.validate_table_structure('audit_logs', ARRAY[
    'id', 'timestamp', 'level', 'message', 'category', 'exception', 'properties',
    'user_id', 'correlation_id', 'machine_name', 'created_at'
]);

-- =============================================
-- Index Validation
-- =============================================

CREATE OR REPLACE FUNCTION payment.validate_indexes()
RETURNS TABLE(
    index_name TEXT,
    table_name TEXT,
    status TEXT,
    details TEXT
) AS $$
BEGIN
    -- Check for critical indexes
    RETURN QUERY
    WITH expected_indexes AS (
        SELECT unnest(ARRAY[
            'ix_payments_status_created_at',
            'ix_payments_team_id_status',
            'ix_transactions_status_created_at',
            'ix_customers_email',
            'ix_audit_entries_entity_id_timestamp',
            'ix_teams_slug'
        ]) as expected_index
    ),
    actual_indexes AS (
        SELECT indexname as actual_index, tablename
        FROM pg_indexes
        WHERE schemaname = 'payment'
    )
    SELECT 
        ei.expected_index::TEXT,
        COALESCE(ai.tablename, 'unknown')::TEXT,
        CASE WHEN ai.actual_index IS NOT NULL THEN 'PASSED' ELSE 'FAILED' END::TEXT,
        CASE WHEN ai.actual_index IS NOT NULL THEN 'Index exists' ELSE 'Index missing' END::TEXT
    FROM expected_indexes ei
    LEFT JOIN actual_indexes ai ON ei.expected_index = ai.actual_index;
END;
$$ LANGUAGE plpgsql;

SELECT * FROM payment.validate_indexes();

-- =============================================
-- Foreign Key Validation
-- =============================================

CREATE OR REPLACE FUNCTION payment.validate_foreign_keys()
RETURNS TABLE(
    constraint_name TEXT,
    table_name TEXT,
    referenced_table TEXT,
    status TEXT
) AS $$
BEGIN
    RETURN QUERY
    SELECT 
        tc.constraint_name::TEXT,
        tc.table_name::TEXT,
        ccu.table_name::TEXT as referenced_table,
        'PASSED'::TEXT as status
    FROM information_schema.table_constraints tc
    JOIN information_schema.key_column_usage kcu 
        ON tc.constraint_name = kcu.constraint_name
        AND tc.table_schema = kcu.table_schema
    JOIN information_schema.constraint_column_usage ccu 
        ON ccu.constraint_name = tc.constraint_name
        AND ccu.table_schema = tc.table_schema
    WHERE tc.constraint_type = 'FOREIGN KEY'
    AND tc.table_schema = 'payment'
    ORDER BY tc.table_name, tc.constraint_name;
END;
$$ LANGUAGE plpgsql;

SELECT * FROM payment.validate_foreign_keys();

-- =============================================
-- Data Integrity Validation
-- =============================================

-- Validate seed data exists
DO $$ 
DECLARE
    teams_count INTEGER;
    config_tables_count INTEGER;
BEGIN 
    SELECT COUNT(*) INTO teams_count FROM payment.teams;
    IF teams_count < 2 THEN
        RAISE WARNING 'Seed data validation: Expected at least 2 teams, found %', teams_count;
    ELSE
        RAISE NOTICE 'Seed data validation: PASSED - Found % teams', teams_count;
    END IF;
    
    -- Check configuration tables exist
    SELECT COUNT(*) INTO config_tables_count
    FROM information_schema.tables
    WHERE table_schema = 'payment' 
    AND table_name IN ('system_configurations', 'payment_method_configurations');
    
    IF config_tables_count < 2 THEN
        RAISE WARNING 'Configuration tables validation: Expected 2 config tables, found %', config_tables_count;
    ELSE
        RAISE NOTICE 'Configuration tables validation: PASSED - Found % config tables', config_tables_count;
    END IF;
END $$;

-- =============================================
-- Performance Validation
-- =============================================

CREATE OR REPLACE FUNCTION payment.validate_query_performance()
RETURNS TABLE(
    test_name TEXT,
    execution_time_ms NUMERIC,
    status TEXT,
    details TEXT
) AS $$
DECLARE
    start_time TIMESTAMP;
    end_time TIMESTAMP;
    duration_ms NUMERIC;
BEGIN
    -- Test 1: Payments by status query
    start_time := clock_timestamp();
    PERFORM COUNT(*) FROM payment.payments WHERE status = 1;
    end_time := clock_timestamp();
    duration_ms := EXTRACT(MILLISECONDS FROM (end_time - start_time));
    
    RETURN QUERY SELECT 
        'Payments by status query'::TEXT,
        duration_ms,
        CASE WHEN duration_ms < 100 THEN 'PASSED' ELSE 'WARNING' END::TEXT,
        format('Query took %s ms', duration_ms)::TEXT;
    
    -- Test 2: Customer lookup by email
    start_time := clock_timestamp();
    PERFORM COUNT(*) FROM payment.customers WHERE email = 'test@example.com';
    end_time := clock_timestamp();
    duration_ms := EXTRACT(MILLISECONDS FROM (end_time - start_time));
    
    RETURN QUERY SELECT 
        'Customer email lookup'::TEXT,
        duration_ms,
        CASE WHEN duration_ms < 50 THEN 'PASSED' ELSE 'WARNING' END::TEXT,
        format('Query took %s ms', duration_ms)::TEXT;
    
    -- Test 3: Audit entries by entity
    start_time := clock_timestamp();
    PERFORM COUNT(*) FROM payment.audit_entries WHERE entity_type = 'Payment';
    end_time := clock_timestamp();
    duration_ms := EXTRACT(MILLISECONDS FROM (end_time - start_time));
    
    RETURN QUERY SELECT 
        'Audit entries by entity type'::TEXT,
        duration_ms,
        CASE WHEN duration_ms < 100 THEN 'PASSED' ELSE 'WARNING' END::TEXT,
        format('Query took %s ms', duration_ms)::TEXT;
END;
$$ LANGUAGE plpgsql;

SELECT * FROM payment.validate_query_performance();

-- =============================================
-- Migration History Validation
-- =============================================

CREATE OR REPLACE FUNCTION payment.validate_migration_history()
RETURNS TABLE(
    migration_id TEXT,
    product_version TEXT,
    applied_date TIMESTAMP,
    status TEXT
) AS $$
BEGIN
    RETURN QUERY
    SELECT 
        mh.migration_id::TEXT,
        mh.product_version::TEXT,
        mh.applied::TIMESTAMP,
        'APPLIED'::TEXT as status
    FROM __ef_migrations_history mh
    ORDER BY mh.applied DESC;
END;
$$ LANGUAGE plpgsql;

SELECT * FROM payment.validate_migration_history();

-- =============================================
-- Security Validation
-- =============================================

CREATE OR REPLACE FUNCTION payment.validate_security_settings()
RETURNS TABLE(
    check_name TEXT,
    status TEXT,
    details TEXT
) AS $$
BEGIN
    -- Check for sensitive data exposure
    RETURN QUERY
    SELECT 
        'Sensitive data protection'::TEXT,
        CASE WHEN COUNT(*) > 0 THEN 'PASSED' ELSE 'WARNING' END::TEXT,
        format('Found %s tables with is_sensitive column', COUNT(*))::TEXT
    FROM information_schema.columns
    WHERE table_schema = 'payment' 
    AND column_name = 'is_sensitive';
    
    -- Check encryption indicators
    RETURN QUERY
    SELECT 
        'Encryption indicators'::TEXT,
        CASE WHEN COUNT(*) > 0 THEN 'PASSED' ELSE 'WARNING' END::TEXT,
        format('Found %s configuration entries for encryption', COUNT(*))::TEXT
    FROM payment.system_configurations
    WHERE key LIKE '%encryption%';
    
    -- Check audit trail completeness
    RETURN QUERY
    SELECT 
        'Audit trail completeness'::TEXT,
        CASE WHEN COUNT(*) > 0 THEN 'PASSED' ELSE 'FAILED' END::TEXT,
        format('Found %s audit-related tables', COUNT(*))::TEXT
    FROM information_schema.tables
    WHERE table_schema = 'payment' 
    AND table_name LIKE 'audit_%';
END;
$$ LANGUAGE plpgsql;

SELECT * FROM payment.validate_security_settings();

-- =============================================
-- Complete Validation Report
-- =============================================

CREATE OR REPLACE FUNCTION payment.generate_validation_report()
RETURNS TABLE(
    category TEXT,
    test_name TEXT,
    status TEXT,
    details TEXT,
    timestamp TIMESTAMP
) AS $$
BEGIN
    -- Schema validation
    RETURN QUERY
    SELECT 
        'Schema'::TEXT,
        'Schema existence'::TEXT,
        CASE WHEN EXISTS (SELECT 1 FROM information_schema.schemata WHERE schema_name = 'payment') 
             THEN 'PASSED' ELSE 'FAILED' END::TEXT,
        'Payment schema validation'::TEXT,
        NOW()::TIMESTAMP;
    
    -- Table count validation
    RETURN QUERY
    SELECT 
        'Tables'::TEXT,
        'Table count'::TEXT,
        CASE WHEN (SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'payment') >= 7
             THEN 'PASSED' ELSE 'FAILED' END::TEXT,
        format('Found %s tables in payment schema', 
               (SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'payment'))::TEXT,
        NOW()::TIMESTAMP;
    
    -- Index validation summary
    RETURN QUERY
    SELECT 
        'Indexes'::TEXT,
        'Critical indexes'::TEXT,
        CASE WHEN (SELECT COUNT(*) FROM pg_indexes WHERE schemaname = 'payment') >= 20
             THEN 'PASSED' ELSE 'WARNING' END::TEXT,
        format('Found %s indexes in payment schema',
               (SELECT COUNT(*) FROM pg_indexes WHERE schemaname = 'payment'))::TEXT,
        NOW()::TIMESTAMP;
    
    -- Foreign key validation summary
    RETURN QUERY
    SELECT 
        'Constraints'::TEXT,
        'Foreign keys'::TEXT,
        CASE WHEN (SELECT COUNT(*) FROM information_schema.table_constraints 
                  WHERE table_schema = 'payment' AND constraint_type = 'FOREIGN KEY') >= 5
             THEN 'PASSED' ELSE 'WARNING' END::TEXT,
        format('Found %s foreign key constraints',
               (SELECT COUNT(*) FROM information_schema.table_constraints 
                WHERE table_schema = 'payment' AND constraint_type = 'FOREIGN KEY'))::TEXT,
        NOW()::TIMESTAMP;
    
    -- Data validation summary
    RETURN QUERY
    SELECT 
        'Data'::TEXT,
        'Seed data'::TEXT,
        CASE WHEN (SELECT COUNT(*) FROM payment.teams) >= 2
             THEN 'PASSED' ELSE 'FAILED' END::TEXT,
        format('Found %s teams in seed data', (SELECT COUNT(*) FROM payment.teams))::TEXT,
        NOW()::TIMESTAMP;
END;
$$ LANGUAGE plpgsql;

-- Generate final validation report
SELECT 
    category,
    test_name,
    status,
    details,
    timestamp
FROM payment.generate_validation_report()
ORDER BY 
    CASE category 
        WHEN 'Schema' THEN 1 
        WHEN 'Tables' THEN 2 
        WHEN 'Indexes' THEN 3 
        WHEN 'Constraints' THEN 4 
        WHEN 'Data' THEN 5 
        ELSE 6 
    END,
    test_name;

-- =============================================
-- Cleanup validation functions (optional)
-- =============================================

/*
DROP FUNCTION IF EXISTS payment.validate_table_structure(TEXT, TEXT[]);
DROP FUNCTION IF EXISTS payment.validate_indexes();
DROP FUNCTION IF EXISTS payment.validate_foreign_keys();
DROP FUNCTION IF EXISTS payment.validate_query_performance();
DROP FUNCTION IF EXISTS payment.validate_migration_history();
DROP FUNCTION IF EXISTS payment.validate_security_settings();
DROP FUNCTION IF EXISTS payment.generate_validation_report();
*/
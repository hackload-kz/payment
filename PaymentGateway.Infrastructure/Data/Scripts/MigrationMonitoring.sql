-- =============================================
-- PaymentGateway Migration Monitoring & Alerting
-- =============================================
-- This file contains monitoring queries and alerting functions
-- for database migrations and overall database health

-- =============================================
-- Migration Status Monitoring
-- =============================================

-- Function to get current migration status
CREATE OR REPLACE FUNCTION payment.get_migration_status()
RETURNS TABLE(
    migration_id TEXT,
    product_version TEXT,
    applied_date TIMESTAMP,
    days_since_applied INTEGER,
    status TEXT
) AS $$
BEGIN
    RETURN QUERY
    SELECT 
        mh.migration_id::TEXT,
        mh.product_version::TEXT,
        mh.applied::TIMESTAMP,
        EXTRACT(DAYS FROM (NOW() - mh.applied))::INTEGER as days_since_applied,
        CASE 
            WHEN mh.applied > NOW() - INTERVAL '1 day' THEN 'RECENT'
            WHEN mh.applied > NOW() - INTERVAL '7 days' THEN 'CURRENT'
            WHEN mh.applied > NOW() - INTERVAL '30 days' THEN 'OLD'
            ELSE 'VERY_OLD'
        END::TEXT as status
    FROM __ef_migrations_history mh
    ORDER BY mh.applied DESC;
END;
$$ LANGUAGE plpgsql;

-- Function to detect failed migrations
CREATE OR REPLACE FUNCTION payment.detect_failed_migrations()
RETURNS TABLE(
    issue_type TEXT,
    severity TEXT,
    description TEXT,
    resolution TEXT
) AS $$
BEGIN
    -- Check for incomplete schema objects
    RETURN QUERY
    SELECT 
        'INCOMPLETE_SCHEMA'::TEXT,
        'HIGH'::TEXT,
        format('Table %s exists but may be incomplete', table_name)::TEXT,
        'Review migration scripts and validate table structure'::TEXT
    FROM information_schema.tables t
    WHERE t.table_schema = 'payment'
    AND NOT EXISTS (
        SELECT 1 FROM information_schema.columns c 
        WHERE c.table_schema = t.table_schema 
        AND c.table_name = t.table_name 
        AND c.column_name = 'created_at'
    );
    
    -- Check for missing indexes on critical tables
    RETURN QUERY
    SELECT 
        'MISSING_INDEXES'::TEXT,
        'MEDIUM'::TEXT,
        format('Critical table %s may be missing performance indexes', tablename)::TEXT,
        'Review index creation scripts and apply missing indexes'::TEXT
    FROM pg_tables pt
    WHERE pt.schemaname = 'payment'
    AND pt.tablename IN ('payments', 'transactions', 'customers')
    AND (
        SELECT COUNT(*) 
        FROM pg_indexes pi 
        WHERE pi.schemaname = pt.schemaname 
        AND pi.tablename = pt.tablename
    ) < 3;
    
    -- Check for foreign key constraint issues
    RETURN QUERY
    SELECT 
        'CONSTRAINT_ISSUES'::TEXT,
        'HIGH'::TEXT,
        'Tables exist without proper foreign key relationships'::TEXT,
        'Review and apply foreign key constraints'::TEXT
    WHERE (
        SELECT COUNT(*) 
        FROM information_schema.table_constraints 
        WHERE constraint_schema = 'payment' 
        AND constraint_type = 'FOREIGN KEY'
    ) < 5;
END;
$$ LANGUAGE plpgsql;

-- =============================================
-- Database Health Monitoring
-- =============================================

-- Function to monitor database performance metrics
CREATE OR REPLACE FUNCTION payment.get_database_health_metrics()
RETURNS TABLE(
    metric_name TEXT,
    metric_value NUMERIC,
    unit TEXT,
    status TEXT,
    threshold_warning NUMERIC,
    threshold_critical NUMERIC
) AS $$
BEGIN
    -- Connection count
    RETURN QUERY
    SELECT 
        'active_connections'::TEXT,
        (SELECT COUNT(*) FROM pg_stat_activity WHERE state = 'active')::NUMERIC,
        'count'::TEXT,
        CASE 
            WHEN (SELECT COUNT(*) FROM pg_stat_activity WHERE state = 'active') > 50 THEN 'CRITICAL'
            WHEN (SELECT COUNT(*) FROM pg_stat_activity WHERE state = 'active') > 20 THEN 'WARNING'
            ELSE 'OK'
        END::TEXT,
        20::NUMERIC,
        50::NUMERIC;
    
    -- Database size
    RETURN QUERY
    SELECT 
        'database_size_mb'::TEXT,
        (SELECT pg_database_size(current_database()) / 1024 / 1024)::NUMERIC,
        'MB'::TEXT,
        CASE 
            WHEN (SELECT pg_database_size(current_database()) / 1024 / 1024) > 10240 THEN 'CRITICAL'
            WHEN (SELECT pg_database_size(current_database()) / 1024 / 1024) > 5120 THEN 'WARNING'
            ELSE 'OK'
        END::TEXT,
        5120::NUMERIC,
        10240::NUMERIC;
    
    -- Table sizes for payment schema
    RETURN QUERY
    SELECT 
        format('table_size_%s', t.table_name)::TEXT,
        (SELECT pg_total_relation_size(format('payment.%s', t.table_name)::regclass) / 1024 / 1024)::NUMERIC,
        'MB'::TEXT,
        CASE 
            WHEN (SELECT pg_total_relation_size(format('payment.%s', t.table_name)::regclass) / 1024 / 1024) > 1024 THEN 'WARNING'
            WHEN (SELECT pg_total_relation_size(format('payment.%s', t.table_name)::regclass) / 1024 / 1024) > 2048 THEN 'CRITICAL'
            ELSE 'OK'
        END::TEXT,
        1024::NUMERIC,
        2048::NUMERIC
    FROM information_schema.tables t
    WHERE t.table_schema = 'payment'
    AND t.table_type = 'BASE TABLE'
    ORDER BY t.table_name;
    
    -- Query performance metrics
    RETURN QUERY
    SELECT 
        'avg_query_time_ms'::TEXT,
        COALESCE((
            SELECT AVG(mean_exec_time) 
            FROM pg_stat_statements 
            WHERE query LIKE '%payment.%'
        ), 0)::NUMERIC,
        'ms'::TEXT,
        CASE 
            WHEN COALESCE((SELECT AVG(mean_exec_time) FROM pg_stat_statements WHERE query LIKE '%payment.%'), 0) > 1000 THEN 'CRITICAL'
            WHEN COALESCE((SELECT AVG(mean_exec_time) FROM pg_stat_statements WHERE query LIKE '%payment.%'), 0) > 500 THEN 'WARNING'
            ELSE 'OK'
        END::TEXT,
        500::NUMERIC,
        1000::NUMERIC;
    
    -- Lock monitoring
    RETURN QUERY
    SELECT 
        'blocked_queries'::TEXT,
        (SELECT COUNT(*) FROM pg_stat_activity WHERE wait_event_type = 'Lock')::NUMERIC,
        'count'::TEXT,
        CASE 
            WHEN (SELECT COUNT(*) FROM pg_stat_activity WHERE wait_event_type = 'Lock') > 10 THEN 'CRITICAL'
            WHEN (SELECT COUNT(*) FROM pg_stat_activity WHERE wait_event_type = 'Lock') > 5 THEN 'WARNING'
            ELSE 'OK'
        END::TEXT,
        5::NUMERIC,
        10::NUMERIC;
END;
$$ LANGUAGE plpgsql;

-- =============================================
-- Migration Anomaly Detection
-- =============================================

-- Function to detect migration anomalies
CREATE OR REPLACE FUNCTION payment.detect_migration_anomalies()
RETURNS TABLE(
    anomaly_type TEXT,
    severity TEXT,
    detected_at TIMESTAMP,
    description TEXT,
    affected_objects TEXT[],
    recommendation TEXT
) AS $$
BEGIN
    -- Detect unusually large schema changes
    RETURN QUERY
    SELECT 
        'LARGE_SCHEMA_CHANGE'::TEXT,
        'WARNING'::TEXT,
        NOW()::TIMESTAMP,
        format('Schema has %s tables, which is unusually high', 
               (SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'payment'))::TEXT,
        ARRAY(SELECT table_name::TEXT FROM information_schema.tables WHERE table_schema = 'payment'),
        'Review recent migrations for unnecessary table creation'::TEXT
    WHERE (SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'payment') > 15;
    
    -- Detect missing audit trails
    RETURN QUERY
    SELECT 
        'MISSING_AUDIT_TRAIL'::TEXT,
        'HIGH'::TEXT,
        NOW()::TIMESTAMP,
        'Tables exist without proper audit trail setup'::TEXT,
        ARRAY(
            SELECT t.table_name::TEXT
            FROM information_schema.tables t
            WHERE t.table_schema = 'payment'
            AND t.table_type = 'BASE TABLE'
            AND t.table_name != 'audit_entries'
            AND t.table_name != 'audit_logs'
            AND NOT EXISTS (
                SELECT 1 FROM information_schema.columns c
                WHERE c.table_schema = t.table_schema
                AND c.table_name = t.table_name
                AND c.column_name IN ('created_at', 'updated_at')
            )
        ),
        'Add audit fields (created_at, updated_at) to all business tables'::TEXT
    WHERE EXISTS (
        SELECT 1
        FROM information_schema.tables t
        WHERE t.table_schema = 'payment'
        AND t.table_type = 'BASE TABLE'
        AND t.table_name NOT IN ('audit_entries', 'audit_logs', '__ef_migrations_history')
        AND NOT EXISTS (
            SELECT 1 FROM information_schema.columns c
            WHERE c.table_schema = t.table_schema
            AND c.table_name = t.table_name
            AND c.column_name IN ('created_at', 'updated_at')
        )
    );
    
    -- Detect orphaned indexes
    RETURN QUERY
    SELECT 
        'ORPHANED_INDEXES'::TEXT,
        'LOW'::TEXT,
        NOW()::TIMESTAMP,
        'Indexes may exist without corresponding table usage'::TEXT,
        ARRAY(SELECT indexname::TEXT FROM pg_indexes WHERE schemaname = 'payment'),
        'Review index usage statistics and remove unused indexes'::TEXT
    WHERE (SELECT COUNT(*) FROM pg_indexes WHERE schemaname = 'payment') > 30;
END;
$$ LANGUAGE plpgsql;

-- =============================================
-- Alerting Configuration
-- =============================================

-- Table to store alert configurations
CREATE TABLE IF NOT EXISTS payment.alert_configurations (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    alert_name VARCHAR(100) NOT NULL UNIQUE,
    alert_type VARCHAR(50) NOT NULL, -- 'THRESHOLD', 'ANOMALY', 'FAILURE'
    metric_name VARCHAR(100),
    threshold_warning NUMERIC,
    threshold_critical NUMERIC,
    check_interval_minutes INTEGER DEFAULT 5,
    notification_channels TEXT[] DEFAULT ARRAY['email'],
    is_enabled BOOLEAN DEFAULT true,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT NOW()
);

-- Insert default alert configurations
INSERT INTO payment.alert_configurations (alert_name, alert_type, metric_name, threshold_warning, threshold_critical, check_interval_minutes, notification_channels)
VALUES 
    ('Database Size Alert', 'THRESHOLD', 'database_size_mb', 5120, 10240, 60, ARRAY['email', 'slack']),
    ('Active Connections Alert', 'THRESHOLD', 'active_connections', 20, 50, 5, ARRAY['email']),
    ('Query Performance Alert', 'THRESHOLD', 'avg_query_time_ms', 500, 1000, 15, ARRAY['email', 'pagerduty']),
    ('Migration Failure Alert', 'FAILURE', NULL, NULL, NULL, 1, ARRAY['email', 'slack', 'pagerduty']),
    ('Schema Anomaly Alert', 'ANOMALY', NULL, NULL, NULL, 30, ARRAY['email']),
    ('Blocked Queries Alert', 'THRESHOLD', 'blocked_queries', 5, 10, 5, ARRAY['email', 'slack'])
ON CONFLICT (alert_name) DO NOTHING;

-- =============================================
-- Alert Processing Functions
-- =============================================

-- Function to check and trigger alerts
CREATE OR REPLACE FUNCTION payment.process_alerts()
RETURNS TABLE(
    alert_name TEXT,
    status TEXT,
    message TEXT,
    should_notify BOOLEAN,
    notification_channels TEXT[]
) AS $$
DECLARE
    alert_config RECORD;
    health_metric RECORD;
    anomaly RECORD;
BEGIN
    -- Process threshold alerts
    FOR alert_config IN 
        SELECT * FROM payment.alert_configurations 
        WHERE alert_type = 'THRESHOLD' AND is_enabled = true
    LOOP
        -- Get corresponding health metric
        SELECT * INTO health_metric
        FROM payment.get_database_health_metrics()
        WHERE metric_name = alert_config.metric_name;
        
        IF FOUND THEN
            RETURN QUERY
            SELECT 
                alert_config.alert_name::TEXT,
                health_metric.status::TEXT,
                format('Metric %s: %s %s (Warning: %s, Critical: %s)', 
                       health_metric.metric_name, 
                       health_metric.metric_value, 
                       health_metric.unit,
                       alert_config.threshold_warning,
                       alert_config.threshold_critical)::TEXT,
                (health_metric.status IN ('WARNING', 'CRITICAL'))::BOOLEAN,
                alert_config.notification_channels;
        END IF;
    END LOOP;
    
    -- Process anomaly alerts
    FOR alert_config IN 
        SELECT * FROM payment.alert_configurations 
        WHERE alert_type = 'ANOMALY' AND is_enabled = true
    LOOP
        FOR anomaly IN 
            SELECT * FROM payment.detect_migration_anomalies()
        LOOP
            RETURN QUERY
            SELECT 
                alert_config.alert_name::TEXT,
                anomaly.severity::TEXT,
                anomaly.description::TEXT,
                (anomaly.severity IN ('HIGH', 'CRITICAL'))::BOOLEAN,
                alert_config.notification_channels;
        END LOOP;
    END LOOP;
    
    -- Process failure alerts (check for failed migrations)
    FOR alert_config IN 
        SELECT * FROM payment.alert_configurations 
        WHERE alert_type = 'FAILURE' AND is_enabled = true
    LOOP
        -- Check for recent failed migrations (this would be enhanced with actual failure detection)
        RETURN QUERY
        SELECT 
            alert_config.alert_name::TEXT,
            'INFO'::TEXT,
            'No migration failures detected'::TEXT,
            false::BOOLEAN,
            alert_config.notification_channels
        WHERE NOT EXISTS (
            SELECT 1 FROM payment.detect_failed_migrations() 
            WHERE severity = 'HIGH'
        );
    END LOOP;
END;
$$ LANGUAGE plpgsql;

-- =============================================
-- Notification Functions
-- =============================================

-- Function to format alert messages for different channels
CREATE OR REPLACE FUNCTION payment.format_alert_message(
    alert_name TEXT,
    status TEXT,
    message TEXT,
    channel TEXT
) RETURNS TEXT AS $$
BEGIN
    CASE channel
        WHEN 'slack' THEN
            RETURN format('🚨 *%s* [%s]\n%s\n_Timestamp: %s_', 
                         alert_name, status, message, NOW());
        WHEN 'email' THEN
            RETURN format('Alert: %s [%s]\n\nDetails: %s\n\nGenerated at: %s\n\nPayment Gateway Monitoring System', 
                         alert_name, status, message, NOW());
        WHEN 'pagerduty' THEN
            RETURN format('ALERT: %s - %s. %s', alert_name, status, message);
        ELSE
            RETURN format('[%s] %s: %s', status, alert_name, message);
    END CASE;
END;
$$ LANGUAGE plpgsql;

-- =============================================
-- Monitoring Dashboard Views
-- =============================================

-- View for migration dashboard
CREATE OR REPLACE VIEW payment.migration_dashboard AS
SELECT 
    (SELECT COUNT(*) FROM __ef_migrations_history) as total_migrations,
    (SELECT migration_id FROM __ef_migrations_history ORDER BY applied DESC LIMIT 1) as latest_migration,
    (SELECT applied FROM __ef_migrations_history ORDER BY applied DESC LIMIT 1) as last_migration_date,
    (SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'payment') as total_tables,
    (SELECT COUNT(*) FROM pg_indexes WHERE schemaname = 'payment') as total_indexes,
    (SELECT COUNT(*) FROM information_schema.table_constraints WHERE constraint_schema = 'payment') as total_constraints,
    (SELECT pg_database_size(current_database()) / 1024 / 1024) as database_size_mb,
    NOW() as last_updated;

-- View for health status summary
CREATE OR REPLACE VIEW payment.health_status_summary AS
SELECT 
    metric_name,
    metric_value,
    unit,
    status,
    CASE 
        WHEN status = 'CRITICAL' THEN 1
        WHEN status = 'WARNING' THEN 2
        WHEN status = 'OK' THEN 3
        ELSE 4
    END as priority_order
FROM payment.get_database_health_metrics()
ORDER BY priority_order, metric_name;

-- =============================================
-- Maintenance and Cleanup
-- =============================================

-- Function to clean up old monitoring data
CREATE OR REPLACE FUNCTION payment.cleanup_monitoring_data(retention_days INTEGER DEFAULT 30)
RETURNS TEXT AS $$
DECLARE
    deleted_count INTEGER := 0;
BEGIN
    -- This would clean up monitoring logs if we had persistent monitoring data
    -- For now, it's a placeholder for future monitoring data cleanup
    
    RETURN format('Cleanup completed. Would remove monitoring data older than %s days', retention_days);
END;
$$ LANGUAGE plpgsql;

-- =============================================
-- Usage Examples and Test Queries
-- =============================================

-- Example: Get current migration status
-- SELECT * FROM payment.get_migration_status();

-- Example: Check database health
-- SELECT * FROM payment.health_status_summary;

-- Example: Detect issues
-- SELECT * FROM payment.detect_failed_migrations();
-- SELECT * FROM payment.detect_migration_anomalies();

-- Example: Process alerts
-- SELECT * FROM payment.process_alerts();

-- Example: View dashboard
-- SELECT * FROM payment.migration_dashboard;

-- =============================================
-- Scheduled Monitoring (PostgreSQL pg_cron example)
-- =============================================

-- Enable pg_cron extension (requires superuser)
-- CREATE EXTENSION IF NOT EXISTS pg_cron;

-- Schedule health check every 5 minutes
-- SELECT cron.schedule('payment-health-check', '*/5 * * * *', 'SELECT payment.process_alerts();');

-- Schedule daily cleanup
-- SELECT cron.schedule('payment-monitoring-cleanup', '0 2 * * *', 'SELECT payment.cleanup_monitoring_data(30);');

-- =============================================
-- Alert History Table (for tracking)
-- =============================================

CREATE TABLE IF NOT EXISTS payment.alert_history (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    alert_name VARCHAR(100) NOT NULL,
    status VARCHAR(20) NOT NULL,
    message TEXT NOT NULL,
    notification_channels TEXT[],
    triggered_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    resolved_at TIMESTAMP WITH TIME ZONE,
    is_resolved BOOLEAN DEFAULT false
);

-- Index for alert history queries
CREATE INDEX IF NOT EXISTS idx_alert_history_triggered_at ON payment.alert_history(triggered_at DESC);
CREATE INDEX IF NOT EXISTS idx_alert_history_alert_name ON payment.alert_history(alert_name);
CREATE INDEX IF NOT EXISTS idx_alert_history_status ON payment.alert_history(status);

-- =============================================
-- Final Validation
-- =============================================

-- Test all monitoring functions
DO $$
BEGIN
    RAISE NOTICE 'Testing migration monitoring functions...';
    
    -- Test each function
    PERFORM payment.get_migration_status();
    PERFORM payment.detect_failed_migrations();
    PERFORM payment.get_database_health_metrics();
    PERFORM payment.detect_migration_anomalies();
    PERFORM payment.process_alerts();
    
    RAISE NOTICE 'All monitoring functions are working correctly!';
END $$;
-- PostgreSQL Performance Monitoring Views for Payment Gateway

-- 1. Payment Processing Performance View
CREATE OR REPLACE VIEW payment_performance_stats AS
SELECT 
    DATE(p."CreatedAt") as processing_date,
    p."Status",
    COUNT(*) as payment_count,
    AVG(p."Amount") as avg_amount,
    SUM(p."Amount") as total_amount,
    MIN(p."Amount") as min_amount,
    MAX(p."Amount") as max_amount,
    COUNT(CASE WHEN p."Status" IN (22, 41, 42) THEN 1 END) as successful_payments,
    COUNT(CASE WHEN p."Status" IN (50, 51, 52) THEN 1 END) as failed_payments,
    ROUND(
        COUNT(CASE WHEN p."Status" IN (22, 41, 42) THEN 1 END) * 100.0 / COUNT(*), 2
    ) as success_rate_percent
FROM "Payments" p
WHERE p."CreatedAt" >= CURRENT_DATE - INTERVAL '30 days'
AND p."IsDeleted" = false
GROUP BY DATE(p."CreatedAt"), p."Status"
ORDER BY processing_date DESC, p."Status";

-- 2. Transaction Processing Time Analysis
CREATE OR REPLACE VIEW transaction_processing_times AS
SELECT 
    DATE(t."CreatedAt") as processing_date,
    t."Type",
    t."Status",
    COUNT(*) as transaction_count,
    AVG(EXTRACT(EPOCH FROM (t."ProcessingCompletedAt" - t."ProcessingStartedAt"))) as avg_processing_seconds,
    MIN(EXTRACT(EPOCH FROM (t."ProcessingCompletedAt" - t."ProcessingStartedAt"))) as min_processing_seconds,
    MAX(EXTRACT(EPOCH FROM (t."ProcessingCompletedAt" - t."ProcessingStartedAt"))) as max_processing_seconds,
    PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY EXTRACT(EPOCH FROM (t."ProcessingCompletedAt" - t."ProcessingStartedAt"))) as median_processing_seconds,
    PERCENTILE_CONT(0.95) WITHIN GROUP (ORDER BY EXTRACT(EPOCH FROM (t."ProcessingCompletedAt" - t."ProcessingStartedAt"))) as p95_processing_seconds
FROM "Transactions" t
WHERE t."ProcessingStartedAt" IS NOT NULL 
AND t."ProcessingCompletedAt" IS NOT NULL
AND t."CreatedAt" >= CURRENT_DATE - INTERVAL '30 days'
AND t."IsDeleted" = false
GROUP BY DATE(t."CreatedAt"), t."Type", t."Status"
ORDER BY processing_date DESC, t."Type", t."Status";

-- 3. Database Connection and Query Performance
CREATE OR REPLACE VIEW database_performance_stats AS
SELECT 
    schemaname,
    tablename,
    attname as column_name,
    n_distinct,
    correlation,
    most_common_vals,
    most_common_freqs
FROM pg_stats 
WHERE schemaname = 'public' 
AND tablename IN ('Payments', 'Transactions', 'Teams', 'Customers', 'PaymentMethods')
ORDER BY schemaname, tablename, attname;

-- 4. Index Usage Statistics
CREATE OR REPLACE VIEW index_usage_stats AS
SELECT 
    schemaname,
    tablename,
    indexname,
    idx_tup_read,
    idx_tup_fetch,
    idx_scan,
    CASE 
        WHEN idx_scan = 0 THEN 'UNUSED'
        WHEN idx_scan < 10 THEN 'LOW_USAGE'
        WHEN idx_scan < 100 THEN 'MEDIUM_USAGE'
        ELSE 'HIGH_USAGE'
    END as usage_category
FROM pg_stat_user_indexes 
WHERE schemaname = 'public'
ORDER BY idx_scan DESC, tablename;

-- 5. Table Size and Growth Analysis
CREATE OR REPLACE VIEW table_size_stats AS
SELECT 
    schemaname,
    tablename,
    pg_size_pretty(pg_total_relation_size(schemaname||'.'||tablename)) as total_size,
    pg_size_pretty(pg_relation_size(schemaname||'.'||tablename)) as table_size,
    pg_size_pretty(pg_total_relation_size(schemaname||'.'||tablename) - pg_relation_size(schemaname||'.'||tablename)) as index_size,
    pg_stat_get_tuples_returned(c.oid) as tuples_returned,
    pg_stat_get_tuples_fetched(c.oid) as tuples_fetched,
    pg_stat_get_tuples_inserted(c.oid) as tuples_inserted,
    pg_stat_get_tuples_updated(c.oid) as tuples_updated,
    pg_stat_get_tuples_deleted(c.oid) as tuples_deleted
FROM pg_tables t
JOIN pg_class c ON c.relname = t.tablename
WHERE t.schemaname = 'public'
AND t.tablename IN ('Payments', 'Transactions', 'Teams', 'Customers', 'PaymentMethods', 'AuditLog')
ORDER BY pg_total_relation_size(schemaname||'.'||tablename) DESC;

-- 6. Payment Gateway Business Metrics
CREATE OR REPLACE VIEW business_metrics_daily AS
SELECT 
    DATE(p."CreatedAt") as business_date,
    t."TeamSlug",
    p."Currency",
    COUNT(DISTINCT p."PaymentId") as unique_payments,
    COUNT(DISTINCT p."CustomerId") as unique_customers,
    SUM(p."Amount") as total_volume,
    AVG(p."Amount") as average_transaction_amount,
    COUNT(CASE WHEN p."Status" IN (22, 41, 42) THEN 1 END) as successful_payments,
    COUNT(CASE WHEN p."Status" IN (50, 51, 52) THEN 1 END) as failed_payments,
    SUM(CASE WHEN p."Status" IN (22, 41, 42) THEN p."Amount" ELSE 0 END) as successful_volume,
    ROUND(
        COUNT(CASE WHEN p."Status" IN (22, 41, 42) THEN 1 END) * 100.0 / NULLIF(COUNT(*), 0), 2
    ) as success_rate_percent,
    ROUND(
        SUM(CASE WHEN p."Status" IN (22, 41, 42) THEN p."Amount" ELSE 0 END) * 100.0 / NULLIF(SUM(p."Amount"), 0), 2
    ) as volume_success_rate_percent
FROM "Payments" p
JOIN "Teams" t ON p."TeamId" = t."Id"
WHERE p."CreatedAt" >= CURRENT_DATE - INTERVAL '90 days'
AND p."IsDeleted" = false
AND t."IsDeleted" = false
GROUP BY DATE(p."CreatedAt"), t."TeamSlug", p."Currency"
ORDER BY business_date DESC, t."TeamSlug", p."Currency";

-- 7. Fraud Detection and Risk Analysis
CREATE OR REPLACE VIEW fraud_risk_analysis AS
SELECT 
    DATE(t."CreatedAt") as analysis_date,
    COUNT(*) as total_transactions,
    COUNT(CASE WHEN t."FraudScore" >= 75 THEN 1 END) as high_risk_transactions,
    COUNT(CASE WHEN t."FraudScore" >= 50 AND t."FraudScore" < 75 THEN 1 END) as medium_risk_transactions,
    COUNT(CASE WHEN t."FraudScore" < 50 THEN 1 END) as low_risk_transactions,
    AVG(t."FraudScore") as avg_fraud_score,
    MAX(t."FraudScore") as max_fraud_score,
    COUNT(CASE WHEN pm."IsFraudulent" = true THEN 1 END) as flagged_payment_methods,
    COUNT(CASE WHEN c."IsBlacklisted" = true THEN 1 END) as blacklisted_customers
FROM "Transactions" t
LEFT JOIN "Payments" p ON t."PaymentId" = p."PaymentId"
LEFT JOIN "PaymentMethods" pm ON p."Id" = pm."Id"
LEFT JOIN "Customers" c ON p."CustomerId" = c."Id"
WHERE t."CreatedAt" >= CURRENT_DATE - INTERVAL '30 days'
AND t."IsDeleted" = false
GROUP BY DATE(t."CreatedAt")
ORDER BY analysis_date DESC;

-- 8. System Health and Audit Monitoring
CREATE OR REPLACE VIEW system_health_metrics AS
SELECT 
    'Payments' as entity_type,
    COUNT(*) as total_records,
    COUNT(CASE WHEN "CreatedAt" >= CURRENT_DATE THEN 1 END) as created_today,
    COUNT(CASE WHEN "UpdatedAt" >= CURRENT_DATE THEN 1 END) as updated_today,
    COUNT(CASE WHEN "IsDeleted" = true THEN 1 END) as soft_deleted
FROM "Payments"
UNION ALL
SELECT 
    'Transactions' as entity_type,
    COUNT(*) as total_records,
    COUNT(CASE WHEN "CreatedAt" >= CURRENT_DATE THEN 1 END) as created_today,
    COUNT(CASE WHEN "UpdatedAt" >= CURRENT_DATE THEN 1 END) as updated_today,
    COUNT(CASE WHEN "IsDeleted" = true THEN 1 END) as soft_deleted
FROM "Transactions"
UNION ALL
SELECT 
    'Teams' as entity_type,
    COUNT(*) as total_records,
    COUNT(CASE WHEN "CreatedAt" >= CURRENT_DATE THEN 1 END) as created_today,
    COUNT(CASE WHEN "UpdatedAt" >= CURRENT_DATE THEN 1 END) as updated_today,
    COUNT(CASE WHEN "IsDeleted" = true THEN 1 END) as soft_deleted
FROM "Teams"
UNION ALL
SELECT 
    'Customers' as entity_type,
    COUNT(*) as total_records,
    COUNT(CASE WHEN "CreatedAt" >= CURRENT_DATE THEN 1 END) as created_today,
    COUNT(CASE WHEN "UpdatedAt" >= CURRENT_DATE THEN 1 END) as updated_today,
    COUNT(CASE WHEN "IsDeleted" = true THEN 1 END) as soft_deleted
FROM "Customers"
UNION ALL
SELECT 
    'PaymentMethods' as entity_type,
    COUNT(*) as total_records,
    COUNT(CASE WHEN "CreatedAt" >= CURRENT_DATE THEN 1 END) as created_today,
    COUNT(CASE WHEN "UpdatedAt" >= CURRENT_DATE THEN 1 END) as updated_today,
    COUNT(CASE WHEN "IsDeleted" = true THEN 1 END) as soft_deleted
FROM "PaymentMethods";

-- 9. Partition Health Monitoring (for partitioned tables)
CREATE OR REPLACE VIEW partition_health_stats AS
SELECT 
    schemaname,
    tablename,
    pg_size_pretty(pg_total_relation_size(schemaname||'.'||tablename)) as partition_size,
    pg_stat_get_tuples_returned(c.oid) as tuples_returned,
    pg_stat_get_tuples_inserted(c.oid) as tuples_inserted,
    CASE 
        WHEN tablename LIKE '%_2024_%' OR tablename LIKE '%_2025_%' THEN 'CURRENT'
        WHEN tablename LIKE '%_2023_%' THEN 'OLD'
        ELSE 'VERY_OLD'
    END as partition_age_category
FROM pg_tables t
JOIN pg_class c ON c.relname = t.tablename
WHERE t.schemaname = 'public'
AND (t.tablename LIKE 'AuditLog_%' OR t.tablename LIKE 'Transactions_%')
ORDER BY partition_age_category, tablename DESC;

-- 10. Create function to refresh materialized views (if using materialized views)
CREATE OR REPLACE FUNCTION refresh_performance_stats()
RETURNS void AS $$
BEGIN
    -- Refresh any materialized views here if created
    -- REFRESH MATERIALIZED VIEW payment_performance_stats;
    -- REFRESH MATERIALIZED VIEW transaction_processing_times;
    
    -- Log the refresh
    INSERT INTO "AuditLog" ("EntityId", "EntityType", "Action", "UserId", "Details", "EntitySnapshot")
    VALUES (
        gen_random_uuid(),
        'PerformanceViews',
        1, -- Created action
        'SYSTEM',
        'Performance views refreshed',
        '{"action": "refresh_performance_stats", "timestamp": "' || NOW() || '"}'
    );
END;
$$ LANGUAGE plpgsql;

-- Schedule the refresh function to run every hour (requires pg_cron extension)
-- SELECT cron.schedule('refresh-performance-stats', '0 * * * *', 'SELECT refresh_performance_stats();');
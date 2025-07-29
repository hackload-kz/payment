-- PostgreSQL Table Partitioning Scripts for Payment Gateway
-- These scripts should be run after the initial migration

-- 1. Partition AuditLog table by timestamp (monthly partitions)
-- First, create partitioned audit log table
DROP TABLE IF EXISTS "AuditLog_Partitioned";

CREATE TABLE "AuditLog_Partitioned" (
    "Id" uuid NOT NULL DEFAULT gen_random_uuid(),
    "EntityId" uuid NOT NULL,
    "EntityType" varchar(100) NOT NULL,
    "Action" integer NOT NULL,
    "UserId" varchar(100),
    "Timestamp" timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "Details" varchar(1000),
    "EntitySnapshot" jsonb NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT "PK_AuditLog_Partitioned" PRIMARY KEY ("Id", "Timestamp")
) PARTITION BY RANGE ("Timestamp");

-- Create monthly partitions for audit log (for current year and next year)
DO $$
DECLARE
    start_date date;
    end_date date;
    partition_name text;
BEGIN
    -- Create partitions for each month from current date
    FOR i IN 0..24 LOOP  -- 24 months (2 years)
        start_date := date_trunc('month', CURRENT_DATE) + (i || ' months')::interval;
        end_date := start_date + interval '1 month';
        partition_name := 'AuditLog_' || to_char(start_date, 'YYYY_MM');
        
        EXECUTE format('CREATE TABLE %I PARTITION OF "AuditLog_Partitioned" 
                       FOR VALUES FROM (%L) TO (%L)',
                       partition_name, start_date, end_date);
        
        -- Create indexes on each partition
        EXECUTE format('CREATE INDEX IX_%s_EntityId ON %I ("EntityId")', 
                       partition_name, partition_name);
        EXECUTE format('CREATE INDEX IX_%s_EntityType_EntityId ON %I ("EntityType", "EntityId")', 
                       partition_name, partition_name);
        EXECUTE format('CREATE INDEX IX_%s_UserId ON %I ("UserId")', 
                       partition_name, partition_name);
        EXECUTE format('CREATE INDEX IX_%s_Action ON %I ("Action")', 
                       partition_name, partition_name);
    END LOOP;
END $$;

-- 2. Partition Transactions table by created_at (monthly partitions for high volume)
DROP TABLE IF EXISTS "Transactions_Partitioned";

CREATE TABLE "Transactions_Partitioned" (
    "Id" uuid NOT NULL DEFAULT gen_random_uuid(),
    "TransactionId" varchar(50) NOT NULL,
    "PaymentId" varchar(50) NOT NULL,
    "Type" integer NOT NULL,
    "Status" integer NOT NULL,
    "Amount" numeric(18,2) NOT NULL,
    "Currency" char(3) NOT NULL,
    "TotalFees" numeric(18,2) NOT NULL DEFAULT 0,
    "ProcessingFee" numeric(18,2) NOT NULL DEFAULT 0,
    "AcquirerFee" numeric(18,2) NOT NULL DEFAULT 0,
    "FraudScore" integer NOT NULL DEFAULT 0,
    "RiskCategory" varchar(20),
    "BankOrderId" varchar(100),
    "AuthorizationCode" varchar(100),
    "ProcessorTransactionId" varchar(100),
    "AcquirerTransactionId" varchar(100),
    "ExternalTransactionId" varchar(100),
    "CardMask" varchar(50),
    "CardType" varchar(100),
    "CardBrand" varchar(100),
    "CardBin" varchar(100),
    "CardLast4" varchar(4),
    "CardCountry" varchar(100),
    "IssuerBank" varchar(100),
    "AcquirerName" varchar(100),
    "ProcessingStartedAt" timestamp with time zone,
    "ProcessingCompletedAt" timestamp with time zone,
    "SettlementDate" date,
    "ExpiresAt" timestamp with time zone,
    "RetryAttempts" integer NOT NULL DEFAULT 0,
    "ThreeDSecureRequired" boolean NOT NULL DEFAULT false,
    "ThreeDSecureStatus" varchar(20),
    "AdditionalData" jsonb NOT NULL DEFAULT '{}',
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "UpdatedAt" timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "CreatedBy" varchar(100),
    "UpdatedBy" varchar(100),
    "IsDeleted" boolean NOT NULL DEFAULT false,
    "DeletedAt" timestamp with time zone,
    "DeletedBy" varchar(100),
    "RowVersion" bytea,
    CONSTRAINT "PK_Transactions_Partitioned" PRIMARY KEY ("Id", "CreatedAt")
) PARTITION BY RANGE ("CreatedAt");

-- Create monthly partitions for transactions
DO $$
DECLARE
    start_date date;
    end_date date;
    partition_name text;
BEGIN
    -- Create partitions for each month from current date
    FOR i IN 0..24 LOOP  -- 24 months (2 years)
        start_date := date_trunc('month', CURRENT_DATE) + (i || ' months')::interval;
        end_date := start_date + interval '1 month';
        partition_name := 'Transactions_' || to_char(start_date, 'YYYY_MM');
        
        EXECUTE format('CREATE TABLE %I PARTITION OF "Transactions_Partitioned" 
                       FOR VALUES FROM (%L) TO (%L)',
                       partition_name, start_date, end_date);
        
        -- Create indexes on each partition
        EXECUTE format('CREATE UNIQUE INDEX IX_%s_TransactionId ON %I ("TransactionId")', 
                       partition_name, partition_name);
        EXECUTE format('CREATE INDEX IX_%s_PaymentId ON %I ("PaymentId")', 
                       partition_name, partition_name);
        EXECUTE format('CREATE INDEX IX_%s_Type_Status ON %I ("Type", "Status")', 
                       partition_name, partition_name);
        EXECUTE format('CREATE INDEX IX_%s_Status_ProcessingStartedAt ON %I ("Status", "ProcessingStartedAt")', 
                       partition_name, partition_name);
    END LOOP;
END $$;

-- 3. Create function to automatically create new partitions
CREATE OR REPLACE FUNCTION create_monthly_partitions(table_name text, months_ahead integer DEFAULT 3)
RETURNS void AS $$
DECLARE
    start_date date;
    end_date date;
    partition_name text;
    current_max_date date;
BEGIN
    -- Get the current maximum partition date
    SELECT 
        MAX(schemaname::text || '.' || tablename::text) 
    INTO current_max_date
    FROM pg_catalog.pg_tables 
    WHERE tablename LIKE table_name || '_%';
    
    -- Create partitions for the next N months
    FOR i IN 1..months_ahead LOOP
        start_date := date_trunc('month', CURRENT_DATE) + (i || ' months')::interval;
        end_date := start_date + interval '1 month';
        partition_name := table_name || '_' || to_char(start_date, 'YYYY_MM');
        
        -- Check if partition already exists
        IF NOT EXISTS (
            SELECT 1 FROM pg_catalog.pg_tables 
            WHERE tablename = partition_name
        ) THEN
            EXECUTE format('CREATE TABLE %I PARTITION OF %I 
                           FOR VALUES FROM (%L) TO (%L)',
                           partition_name, table_name, start_date, end_date);
            
            -- Add appropriate indexes based on table type
            IF table_name = 'AuditLog_Partitioned' THEN
                EXECUTE format('CREATE INDEX IX_%s_EntityId ON %I ("EntityId")', 
                               partition_name, partition_name);
                EXECUTE format('CREATE INDEX IX_%s_EntityType_EntityId ON %I ("EntityType", "EntityId")', 
                               partition_name, partition_name);
            ELSIF table_name = 'Transactions_Partitioned' THEN
                EXECUTE format('CREATE UNIQUE INDEX IX_%s_TransactionId ON %I ("TransactionId")', 
                               partition_name, partition_name);
                EXECUTE format('CREATE INDEX IX_%s_PaymentId ON %I ("PaymentId")', 
                               partition_name, partition_name);
            END IF;
            
            RAISE NOTICE 'Created partition: %', partition_name;
        END IF;
    END LOOP;
END;
$$ LANGUAGE plpgsql;

-- 4. Create scheduled job to automatically create partitions (requires pg_cron extension)
-- SELECT cron.schedule('create-partitions', '0 0 1 * *', 'SELECT create_monthly_partitions(''AuditLog_Partitioned'', 3); SELECT create_monthly_partitions(''Transactions_Partitioned'', 3);');

-- 5. Create function to drop old partitions (for data retention)
CREATE OR REPLACE FUNCTION drop_old_partitions(table_name text, months_to_keep integer DEFAULT 24)
RETURNS void AS $$
DECLARE
    partition_record record;
    cutoff_date date;
BEGIN
    cutoff_date := date_trunc('month', CURRENT_DATE) - (months_to_keep || ' months')::interval;
    
    FOR partition_record IN
        SELECT schemaname, tablename 
        FROM pg_catalog.pg_tables 
        WHERE tablename LIKE table_name || '_%'
        AND tablename < table_name || '_' || to_char(cutoff_date, 'YYYY_MM')
    LOOP
        EXECUTE format('DROP TABLE IF EXISTS %I.%I', 
                       partition_record.schemaname, partition_record.tablename);
        RAISE NOTICE 'Dropped old partition: %', partition_record.tablename;
    END LOOP;
END;
$$ LANGUAGE plpgsql;

-- Usage examples:
-- SELECT create_monthly_partitions('AuditLog_Partitioned', 6);
-- SELECT drop_old_partitions('AuditLog_Partitioned', 24);
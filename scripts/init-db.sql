-- Initialize payment gateway database
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

-- Create merchants table
CREATE TABLE IF NOT EXISTS merchants (
    terminal_key VARCHAR(20) PRIMARY KEY,
    password VARCHAR(50) NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT true,
    created_date TIMESTAMP NOT NULL DEFAULT NOW(),
    last_login_date TIMESTAMP
);

-- Create payments table
CREATE TABLE IF NOT EXISTS payments (
    payment_id VARCHAR(20) PRIMARY KEY,
    order_id VARCHAR(36) NOT NULL,
    terminal_key VARCHAR(20) NOT NULL,
    amount BIGINT NOT NULL,
    current_status INTEGER NOT NULL DEFAULT 0,
    description VARCHAR(500),
    customer_key VARCHAR(36),
    pay_type VARCHAR(1),
    language VARCHAR(2),
    notification_url TEXT,
    success_url TEXT,
    fail_url TEXT,
    created_date TIMESTAMP NOT NULL DEFAULT NOW(),
    updated_date TIMESTAMP NOT NULL DEFAULT NOW(),
    expiration_date TIMESTAMP,
    attempt_count INTEGER NOT NULL DEFAULT 0,
    max_attempts INTEGER NOT NULL DEFAULT 3,
    recurrent BOOLEAN NOT NULL DEFAULT false,
    payment_url TEXT,
    error_code VARCHAR(10),
    message TEXT,
    data_json TEXT,
    receipt_json TEXT,
    shops_json TEXT
);

-- Create payment status history table
CREATE TABLE IF NOT EXISTS payment_status_history (
    id SERIAL PRIMARY KEY,
    payment_id VARCHAR(20) NOT NULL,
    status INTEGER NOT NULL,
    timestamp TIMESTAMP NOT NULL DEFAULT NOW(),
    error_code VARCHAR(10),
    message VARCHAR(500),
    FOREIGN KEY (payment_id) REFERENCES payments(payment_id) ON DELETE CASCADE
);

-- Create indexes
CREATE INDEX IF NOT EXISTS idx_payments_order_id ON payments(order_id);
CREATE INDEX IF NOT EXISTS idx_payments_terminal_key ON payments(terminal_key);
CREATE INDEX IF NOT EXISTS idx_payments_order_terminal ON payments(order_id, terminal_key);
CREATE INDEX IF NOT EXISTS idx_payment_status_history_payment_id ON payment_status_history(payment_id);
CREATE INDEX IF NOT EXISTS idx_payment_status_history_timestamp ON payment_status_history(timestamp);

-- Insert test merchants
INSERT INTO merchants (terminal_key, password, is_active) VALUES 
('TEST_TERMINAL_1', 'test_password_1', true),
('TEST_TERMINAL_2', 'test_password_2', true),
('DEMO_TERMINAL', 'demo_password', true)
ON CONFLICT (terminal_key) DO NOTHING;
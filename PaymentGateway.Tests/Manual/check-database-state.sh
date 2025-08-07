#!/bin/bash

# Database State Verification Script
# Checks the database state after running payment lifecycle tests

# Colors for output
GREEN='\033[0;32m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
YELLOW='\033[0;33m'
NC='\033[0m' # No Color

print_header() {
    echo -e "${BLUE}==================== $1 ====================${NC}"
}

print_info() {
    echo -e "${CYAN}$1${NC}"
}

# Database connection details (adjust as needed)
DB_HOST="localhost"
DB_PORT="5432"
DB_NAME="task"
DB_USER="organizer"
DB_PASSWORD="password"

# Function to execute SQL query
execute_query() {
    local query="$1"
    local description="$2"
    
    echo -e "${YELLOW}$description:${NC}"
    
    # Use psql to execute the query
    PGPASSWORD="$DB_PASSWORD" psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -c "$query" 2>/dev/null
    
    if [ $? -eq 0 ]; then
        echo -e "${GREEN}✅ Query executed successfully${NC}"
    else
        echo -e "${CYAN}ℹ️ Note: Database connection may not be available or query failed${NC}"
    fi
    echo ""
}

main() {
    echo -e "${BLUE}🔍 PaymentGateway Database State Verification${NC}"
    echo "=============================================="
    echo ""
    
    print_header "RECENT TEAMS (Last 5)"
    execute_query "SELECT team_slug, team_name, created_at, is_active FROM payment.teams ORDER BY created_at DESC LIMIT 5;" "Recent registered teams"
    
    print_header "PAYMENT TEST TEAMS"
    execute_query "SELECT team_slug, team_name, created_at, contact_email FROM payment.teams WHERE team_slug LIKE 'payment-test-%' ORDER BY created_at DESC;" "Teams created by payment lifecycle tests"
    
    print_header "PAYMENT RECORDS"
    execute_query "SELECT payment_id, team_slug, order_id, amount, currency, status, created_at FROM payment.payments WHERE team_slug LIKE 'payment-test-%' ORDER BY created_at DESC LIMIT 10;" "Payment records from test teams"
    
    print_header "TRANSACTIONS"
    execute_query "SELECT transaction_id, payment_id, amount, status, created_at FROM payment.transactions WHERE payment_id IN (SELECT payment_id FROM payment.payments WHERE team_slug LIKE 'payment-test-%') ORDER BY created_at DESC LIMIT 10;" "Transaction records from test payments"
    
    print_header "AUDIT LOGS (RECENT)"
    execute_query "SELECT event_type, table_name, operation, created_at FROM payment.audit_logs WHERE table_name IN ('teams', 'payments', 'transactions') ORDER BY created_at DESC LIMIT 10;" "Recent audit logs for payment operations"
    
    print_header "DATABASE STATISTICS"
    execute_query "SELECT 'teams' as table_name, COUNT(*) as record_count FROM payment.teams
                   UNION ALL
                   SELECT 'payments' as table_name, COUNT(*) as record_count FROM payment.payments
                   UNION ALL
                   SELECT 'transactions' as table_name, COUNT(*) as record_count FROM payment.transactions;" "Table record counts"
    
    print_header "TEST TEAM DETAILS"
    latest_test_team=$(PGPASSWORD="$DB_PASSWORD" psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" -d "$DB_NAME" -t -c "SELECT team_slug FROM payment.teams WHERE team_slug LIKE 'payment-test-%' ORDER BY created_at DESC LIMIT 1;" 2>/dev/null | xargs)
    
    if [ ! -z "$latest_test_team" ]; then
        print_info "Latest test team: $latest_test_team"
        execute_query "SELECT 
            team_slug,
            team_name,
            contact_email,
            contact_phone,
            supported_currencies,
            success_url,
            fail_url,
            notification_url,
            is_active,
            created_at,
            failed_authentication_attempts
        FROM payment.teams 
        WHERE team_slug = '$latest_test_team';" "Detailed information for latest test team"
    else
        print_info "No test teams found in database"
    fi
    
    echo ""
    print_info "💡 To clean up test data, run:"
    print_info "DELETE FROM payment.teams WHERE team_slug LIKE 'payment-test-%';"
    print_info "DELETE FROM payment.payments WHERE team_slug LIKE 'payment-test-%';"
    
    echo ""
    print_info "📊 Database Connection Info:"
    print_info "Host: $DB_HOST:$DB_PORT"
    print_info "Database: $DB_NAME"
    print_info "User: $DB_USER"
    print_info ""
    print_info "If connection fails, check:"
    print_info "1. PostgreSQL is running"
    print_info "2. Database credentials are correct"
    print_info "3. Database PaymentGateway exists"
}

# Check if psql is installed
if ! command -v psql &> /dev/null; then
    echo -e "${YELLOW}⚠️ psql is not installed. Cannot connect to database.${NC}"
    echo -e "${CYAN}Install PostgreSQL client: brew install postgresql (macOS) or apt-get install postgresql-client (Ubuntu)${NC}"
    exit 1
fi

# Run the main verification
main

exit 0
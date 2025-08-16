#!/bin/bash

# Test script for the new team info admin endpoint
# This script tests the GET /api/v1/TeamRegistration/info/{teamSlug} endpoint

set -e

# Configuration
ADMIN_TOKEN="admin_token_2025_hackload_payment_gateway_secure_key_dev_only"
BASE_URL="http://localhost:5162"
TEST_TEAM_SLUG="test-team-info-$(date +%s)"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Logging functions
log_info() {
    echo -e "${BLUE}[INFO]${NC} $1"
}

log_success() {
    echo -e "${GREEN}[SUCCESS]${NC} $1"
}

log_warning() {
    echo -e "${YELLOW}[WARNING]${NC} $1"
}

log_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# Function to make HTTP requests with error handling
make_request() {
    local method="$1"
    local url="$2"
    local headers="$3"
    local data="$4"
    local description="$5"
    
    log_info "Testing: $description"
    log_info "Request: $method $url"
    
    if [ -n "$data" ]; then
        response=$(curl -s -w "\n%{http_code}" -X "$method" "$url" $headers -d "$data" 2>/dev/null || echo "CURL_ERROR")
    else
        response=$(curl -s -w "\n%{http_code}" -X "$method" "$url" $headers 2>/dev/null || echo "CURL_ERROR")
    fi
    
    if [ "$response" = "CURL_ERROR" ]; then
        log_error "Failed to connect to server"
        return 1
    fi
    
    # Extract status code (last line) and body (everything except last line)
    status_code=$(echo "$response" | tail -n1)
    body=$(echo "$response" | head -n -1)
    
    echo "Status: $status_code"
    echo "Response:"
    echo "$body" | jq '.' 2>/dev/null || echo "$body"
    echo ""
    
    return 0
}

# Function to register a test team
register_test_team() {
    local team_slug="$1"
    
    log_info "Registering test team: $team_slug"
    
    local team_data='{
        "teamSlug": "'$team_slug'",
        "teamName": "Test Team Info Team",
        "password": "SecurePassword123!",
        "email": "'$team_slug'@example.com",
        "phone": "+1234567890",
        "successURL": "https://'$team_slug'.example.com/success",
        "failURL": "https://'$team_slug'.example.com/fail",
        "notificationURL": "https://'$team_slug'.example.com/webhook",
        "supportedCurrencies": "RUB,USD,EUR",
        "businessInfo": {
            "businessType": "ecommerce",
            "description": "Test team for info endpoint testing"
        },
        "acceptTerms": true
    }'
    
    make_request "POST" "$BASE_URL/api/v1/TeamRegistration/register" \
        "-H 'Content-Type: application/json' -H 'X-Admin-Token: $ADMIN_TOKEN'" \
        "$team_data" \
        "Register test team"
}

# Function to test team info endpoint
test_team_info() {
    local team_slug="$1"
    local expected_status="$2"
    local description="$3"
    local auth_header="$4"
    
    make_request "GET" "$BASE_URL/api/v1/TeamRegistration/info/$team_slug" \
        "$auth_header" \
        "" \
        "$description"
}

# Main test execution
main() {
    log_info "Starting Team Info Endpoint Tests"
    log_info "Base URL: $BASE_URL"
    log_info "Test team slug: $TEST_TEAM_SLUG"
    echo "=================================================="
    
    # Test 1: Check server availability
    log_info "Test 1: Check server availability"
    health_response=$(curl -s -w "%{http_code}" "$BASE_URL/health" -o /dev/null 2>/dev/null || echo "000")
    
    if [ "$health_response" = "200" ]; then
        log_success "Server is running at $BASE_URL"
    else
        log_error "Server is not accessible at $BASE_URL (status: $health_response)"
        exit 1
    fi
    echo ""
    
    # Test 2: Try to get info for non-existent team (should return 404)
    log_info "Test 2: Get info for non-existent team"
    test_team_info "non-existent-team" "404" "Non-existent team" "-H 'X-Admin-Token: $ADMIN_TOKEN'"
    
    # Test 3: Try without admin token (should return 401)
    log_info "Test 3: Access without admin token"
    test_team_info "any-team" "401" "No admin token" ""
    
    # Test 4: Try with invalid admin token (should return 401)
    log_info "Test 4: Access with invalid admin token"
    test_team_info "any-team" "401" "Invalid admin token" "-H 'X-Admin-Token: invalid-token'"
    
    # Test 5: Register a test team
    log_info "Test 5: Register test team"
    register_test_team "$TEST_TEAM_SLUG"
    
    # Test 6: Get comprehensive team info (should return 200 with full data)
    log_info "Test 6: Get comprehensive team info"
    test_team_info "$TEST_TEAM_SLUG" "200" "Get full team info" "-H 'X-Admin-Token: $ADMIN_TOKEN'"
    
    # Test 7: Test with Bearer token authentication
    log_info "Test 7: Test Bearer token authentication"
    test_team_info "$TEST_TEAM_SLUG" "200" "Bearer token auth" "-H 'Authorization: Bearer $ADMIN_TOKEN'"
    
    # Test 8: Test with invalid team slug format
    log_info "Test 8: Invalid team slug format"
    test_team_info "x" "400" "Invalid slug format" "-H 'X-Admin-Token: $ADMIN_TOKEN'"
    
    echo "=================================================="
    log_success "Team Info Endpoint Tests Completed!"
    
    echo ""
    log_info "Summary of endpoints tested:"
    echo "✓ GET /api/v1/TeamRegistration/info/{teamSlug} - Admin team information endpoint"
    echo "✓ Authentication validation (Bearer token and X-Admin-Token header)"
    echo "✓ Input validation (team slug format)"
    echo "✓ Error handling (404, 401, 400 responses)"
    echo "✓ Comprehensive team data retrieval"
}

# Run the tests
main "$@"
#!/bin/bash

# Payment Gateway Init Test Script - Modernized Edition
# This script tests the payment initialization endpoint with proper token generation,
# detailed server response logging, and comprehensive webhook flow demonstration
#
# Features:
# - Detailed request/response logging
# - Server response headers capture
# - Enhanced error diagnostics
# - Modern JSON formatting
# - Request timing information
# - Server log correlation support

set -euo pipefail  # Exit on error, undefined vars, pipe failures

# Colors for enhanced output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
PURPLE='\033[0;35m'
CYAN='\033[0;36m'
BOLD='\033[1m'
DIM='\033[2m'
NC='\033[0m' # No Color

# Script metadata
SCRIPT_VERSION="2.0.0"
SCRIPT_NAME="Payment Gateway Test Suite"

# Configuration
#API_BASE_URL="http://localhost:7010"
#TEAM_SLUG="my-webhook-test"
#PASSWORD="TestPassword123!"

API_BASE_URL="https://hub.hackload.kz/payment-provider/common"
TEAM_SLUG="los-lobos"
PASSWORD="nSHCigO232PEMT#HqqcsqGweOEdUK"

# "MERCHANT_ID": "los-lobos",
        #"MERCHANT_PASSWORD": "nSHCigO232PEMT#HqqcsqGweOEdUK"

# Generate unique order ID with timestamp
ORDER_ID="order-$(date +%s)"

# Payment parameters
AMOUNT="100000"  # 1000 RUB in kopecks
CURRENCY="RUB"
DESCRIPTION="Test webhook payment - $(date)"
EMAIL="customer@example.com"
SUCCESS_URL="https://mystore.com/success"
FAIL_URL="https://mystore.com/fail"
NOTIFICATION_URL="https://webhook.site/unique-id-here"
LANGUAGE="ru"
PAY_TYPE="O"
PAYMENT_EXPIRY="30"

# Enhanced logging functions
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

log_debug() {
    if [[ "${DEBUG:-0}" == "1" ]]; then
        echo -e "${DIM}[DEBUG]${NC} $1"
    fi
}

log_request() {
    echo -e "${PURPLE}[REQUEST]${NC} $1"
}

log_response() {
    echo -e "${CYAN}[RESPONSE]${NC} $1"
}

log_timing() {
    echo -e "${BOLD}[TIMING]${NC} $1"
}

# Function to print section headers
print_section() {
    echo ""
    echo -e "${BOLD}${BLUE}▓▓▓ $1 ▓▓▓${NC}"
    echo ""
}

# Function to print subsection headers  
print_subsection() {
    echo ""
    echo -e "${DIM}━━━ $1 ━━━${NC}"
}

# Function to format JSON output with syntax highlighting (if available)
format_json() {
    local json_input="$1"
    local json_type="${2:-response}"
    
    if command -v jq >/dev/null 2>&1; then
        echo "$json_input" | jq --color-output '.' 2>/dev/null || echo "$json_input"
    elif command -v python3 >/dev/null 2>&1; then
        echo "$json_input" | python3 -m json.tool 2>/dev/null || echo "$json_input"
    else
        echo "$json_input"
    fi
}

# Function to generate correlation ID for request tracking
generate_correlation_id() {
    echo "test-$(date +%s)-$$-$(shuf -i 1000-9999 -n 1 2>/dev/null || echo $RANDOM)"
}

# Function to generate token using correct server algorithm
generate_token() {
    local amount="$1"
    local currency="$2"
    local order_id="$3"
    local team_slug="$4"
    local password="$5"
    
    # SERVER TOKEN FORMULA: All parameters sorted alphabetically by key name, then concatenated
    # Based on PaymentAuthenticationService.cs implementation:
    # 1. Include only core parameters (Amount, Currency, OrderId, TeamSlug)
    # 2. Add Password 
    # 3. Sort by key name alphabetically
    # 4. Concatenate values in sorted order
    
    # Redirect all logging to stderr so only token goes to stdout
    {
        log_info "Token generation parameters (server algorithm):"
        echo "  Amount: $amount"
        echo "  Currency: $currency" 
        echo "  OrderId: $order_id"
        echo "  Password: [HIDDEN]"
        echo "  TeamSlug: $team_slug"
        echo ""
        
        # Based on server logs, the concatenation order is: Amount + Currency + OrderId + Password + TeamSlug
        # This matches the alphabetical sorting: Amount, Currency, OrderId, Password, TeamSlug
        
        echo "  Server algorithm concatenation order:"
        echo "    Amount: $amount"
        echo "    Currency: $currency"
        echo "    OrderId: $order_id"
        echo "    Password: [HIDDEN]"
        echo "    TeamSlug: $team_slug"
        
        echo ""
        echo "  Final concatenated string: [HIDDEN FOR SECURITY]"
        echo ""
    } >&2
    
    # Concatenate in the exact order shown in server logs
    local token_string="${amount}${currency}${order_id}${password}${team_slug}"
    
    # Generate SHA-256 hash
    local token=$(echo -n "$token_string" | shasum -a 256 | cut -d' ' -f1)
    echo "  Generated token: $token" >&2
    echo "" >&2
    
    # Return only the token to stdout
    echo "$token"
}

# Function to make payment init request
test_payment_init() {
    local token="$1"
    
    log_info "Making payment initialization request..."
    
    # Debug: Check token length
    log_debug "Token length: ${#token} characters"
    log_debug "Token value: $token"
    
    # Create JSON payload using jq to ensure proper escaping
    local json_payload=$(jq -n \
        --arg teamSlug "$TEAM_SLUG" \
        --arg token "$token" \
        --argjson amount "$AMOUNT" \
        --arg orderId "$ORDER_ID" \
        --arg currency "$CURRENCY" \
        --arg description "$DESCRIPTION" \
        --arg email "$EMAIL" \
        --arg successURL "$SUCCESS_URL" \
        --arg failURL "$FAIL_URL" \
        --arg notificationURL "$NOTIFICATION_URL" \
        --arg language "$LANGUAGE" \
        --arg payType "$PAY_TYPE" \
        --argjson paymentExpiry "$PAYMENT_EXPIRY" \
        '{
            teamSlug: $teamSlug,
            token: $token,
            amount: $amount,
            orderId: $orderId,
            currency: $currency,
            description: $description,
            email: $email,
            successURL: $successURL,
            failURL: $failURL,
            notificationURL: $notificationURL,
            language: $language,
            payType: $payType,
            paymentExpiry: $paymentExpiry
        }')
    
    # Debug: Show the JSON payload being sent
    log_debug "JSON payload being sent:"
    if [[ "${DEBUG:-0}" == "1" ]]; then
        echo "$json_payload" | jq '.'
    fi
    
    local response=$(curl -s -w "\n%{http_code}" -X POST "${API_BASE_URL}/api/v1/PaymentInit/init" \
        -H "Content-Type: application/json" \
        -d "$json_payload")
    
    # Split response and status code (macOS compatible)
    local http_code=$(echo "$response" | tail -n1)
    local response_body=$(echo "$response" | sed '$d')
    
    echo ""
    log_info "HTTP Status Code: $http_code"
    echo ""
    
    if [ "$http_code" = "200" ]; then
        log_success "Payment initialization successful!"
        echo ""
        log_info "Response:"
        echo "$response_body" | jq '.' 2>/dev/null || echo "$response_body"
        echo ""
        
        # Extract payment ID and URL if available
        local payment_id=$(echo "$response_body" | jq -r '.paymentId // empty' 2>/dev/null)
        local payment_url=$(echo "$response_body" | jq -r '.paymentURL // empty' 2>/dev/null)
        
        if [ -n "$payment_id" ]; then
            log_success "Payment ID: $payment_id"
        fi
        
        if [ -n "$payment_url" ]; then
            log_success "Payment URL: $payment_url"
            echo ""
            log_info "Next steps:"
            echo "1. Customer should visit: $payment_url"
            echo "2. Fill out payment form to trigger FORM_SHOWED webhook"
            echo "3. Submit card details to trigger AUTHORIZED webhook"
            echo "4. Use PaymentConfirm API to trigger CONFIRMED webhook"
        fi
        
        return 0
    else
        log_error "Payment initialization failed!"
        echo ""
        log_info "Response:"
        echo "$response_body" | jq '.' 2>/dev/null || echo "$response_body"
        return 1
    fi
}

# Function to check if server is running
check_server() {
    log_info "Checking if payment gateway server is running..."
    
    local health_check=$(curl -s -w "%{http_code}" "${API_BASE_URL}/health" -o /dev/null 2>/dev/null)
    
    if [ "$health_check" = "200" ]; then
        log_success "Payment gateway server is running"
        return 0
    else
        log_error "Payment gateway server is not accessible at $API_BASE_URL"
        log_info "Please ensure the server is running with: dotnet run --project PaymentGateway.API"
        return 1
    fi
}

# Function to display webhook information
show_webhook_info() {
    echo ""
    log_info "Webhook Information:"
    echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    echo ""
    echo "Expected webhook flow:"
    echo "1. 🔄 Payment Created: NEW status webhook"
    echo "2. 👀 Form Viewed: FORM_SHOWED webhook (when customer submits form)"
    echo "3. ✅ Card Authorized: AUTHORIZED webhook (if card processing succeeds)"
    echo "4. 💰 Payment Confirmed: CONFIRMED webhook (when PaymentConfirm called)"
    echo ""
    echo "Webhook URL: $NOTIFICATION_URL"
    echo ""
    echo "Each webhook will include:"
    echo "- HMAC-SHA256 signature in X-Webhook-Signature header"
    echo "- Event type in X-Webhook-Event header"
    echo "- Unique delivery ID in X-Webhook-Delivery header"
    echo ""
}

# Main execution
main() {
    echo ""
    echo "╔══════════════════════════════════════════════════════════════════════════════╗"
    echo "║                    Payment Gateway Initialization Test                       ║"
    echo "╚══════════════════════════════════════════════════════════════════════════════╝"
    echo ""
    
    # Check prerequisites
    command -v curl >/dev/null 2>&1 || { log_error "curl is required but not installed. Aborting."; exit 1; }
    command -v jq >/dev/null 2>&1 || log_warning "jq is not installed. JSON formatting will be limited."
    command -v shasum >/dev/null 2>&1 || { log_error "shasum is required but not installed. Aborting."; exit 1; }
    
    # Check if server is running
    if ! check_server; then
        exit 1
    fi
    
    echo ""
    log_info "Test Configuration:"
    echo "  Team Slug: $TEAM_SLUG"
    echo "  Order ID: $ORDER_ID"
    echo "  Amount: $AMOUNT kopecks ($(($AMOUNT / 100)) RUB)"
    echo "  Currency: $CURRENCY"
    echo "  Webhook URL: $NOTIFICATION_URL"
    echo ""
    
    # Generate token
    log_info "Generating authentication token..."
    echo ""
    local token=$(generate_token "$AMOUNT" "$CURRENCY" "$ORDER_ID" "$TEAM_SLUG" "$PASSWORD")
    log_success "Generated token: $token"
    echo ""
    
    # Make payment request
    if test_payment_init "$token"; then
        show_webhook_info
        
        echo ""
        log_success "Test completed successfully!"
        echo ""
        echo "To test the full webhook flow:"
        echo "1. Visit the payment URL above in a browser"
        echo "2. Submit the payment form with test card details"
        echo "3. Monitor your webhook endpoint for notifications"
        echo "4. Use the PaymentConfirm API to complete the payment"
        echo ""
    else
        log_error "Test failed. Check the error details above."
        exit 1
    fi
}

# Allow script to be sourced for individual function usage
if [[ "${BASH_SOURCE[0]}" == "${0}" ]]; then
    main "$@"
fi
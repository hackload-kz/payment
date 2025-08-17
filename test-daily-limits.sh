#!/bin/bash

# Business Rules Engine Daily Limits Test Script
# This script tests team-specific daily limits functionality

set -e

BASE_URL="http://localhost:5162"
TIMESTAMP=$(date +%Y%m%d-%H%M%S)
TEAM_SLUG="test-limits-${TIMESTAMP}"
PASSWORD="TestPassword123@"
ADMIN_TOKEN="admin_token_2025_hackload_payment_gateway_secure_key_dev_only"

echo "=== Business Rules Engine Daily Limits Test ==="
echo "Testing against: $BASE_URL"
echo "Team slug: $TEAM_SLUG"
echo ""

# Function to generate simple token (simplified for testing)
generate_token() {
    local team_slug="$1"
    local password="$2"
    local amount="$3"
    local order_id="$4"
    local currency="${5:-RUB}"
    
    # Simple token generation: Amount + Currency + OrderId + Password + TeamSlug
    local token_string="${amount}${currency}${order_id}${password}${team_slug}"
    echo -n "$token_string" | sha256sum | cut -d' ' -f1
}

# Function to make payment init request
init_payment() {
    local amount="$1"
    local order_id="$2"
    local expected_success="$3"
    
    local token=$(generate_token "$TEAM_SLUG" "$PASSWORD" "$amount" "$order_id")
    
    echo "Testing payment: Amount=$amount, OrderId=$order_id"
    
    local response=$(curl -s -X POST "$BASE_URL/api/v1/PaymentInit/init" \
        -H "Content-Type: application/json" \
        -d "{
            \"teamSlug\": \"$TEAM_SLUG\",
            \"token\": \"$token\",
            \"amount\": $amount,
            \"orderId\": \"$order_id\",
            \"currency\": \"RUB\",
            \"description\": \"Daily limits test payment\",
            \"successURL\": \"https://example.com/success\",
            \"failURL\": \"https://example.com/fail\",
            \"notificationURL\": \"https://example.com/webhook\",
            \"paymentExpiry\": 30,
            \"language\": \"ru\",
            \"email\": \"test@example.com\"
        }")
    
    local success=$(echo "$response" | jq -r '.success // false')
    local message=$(echo "$response" | jq -r '.message // ""')
    local payment_id=$(echo "$response" | jq -r '.paymentId // ""')
    
    if [ "$success" = "$expected_success" ]; then
        if [ "$expected_success" = "true" ]; then
            echo "✓ Payment successful as expected: $payment_id"
        else
            echo "✓ Payment rejected as expected: $message"
        fi
    else
        if [ "$expected_success" = "true" ]; then
            echo "❌ Payment should have succeeded but failed: $message"
        else
            echo "❌ Payment should have failed but succeeded: $payment_id"
        fi
    fi
    
    echo "Response: $response"
    echo ""
    
    # Return payment ID for further operations
    echo "$payment_id"
}

# Step 1: Register team with daily limits
echo "--- Step 1: Registering test team ---"

TEAM_RESPONSE=$(curl -s -X POST "$BASE_URL/api/v1/TeamRegistration/register" \
    -H "Content-Type: application/json" \
    -H "X-Admin-Token: $ADMIN_TOKEN" \
    -d "{
        \"teamSlug\": \"$TEAM_SLUG\",
        \"teamName\": \"Daily Limits Test Team (Millions)\",
        \"password\": \"$PASSWORD\",
        \"email\": \"test-${TIMESTAMP}@example.com\",
        \"successURL\": \"https://example.com/success\",
        \"failURL\": \"https://example.com/fail\",
        \"notificationURL\": \"https://example.com/webhook\",
        \"supportedCurrencies\": \"RUB,USD\",
        \"acceptTerms\": true
    }")

TEAM_SUCCESS=$(echo "$TEAM_RESPONSE" | jq -r '.success // false')
TEAM_ID=$(echo "$TEAM_RESPONSE" | jq -r '.teamId // ""')

if [ "$TEAM_SUCCESS" = "true" ]; then
    echo "✓ Team registered successfully: $TEAM_ID"
    echo "  Daily Limit: 500,000,000 kopecks (5,000,000 RUB = 5M RUB)"
    echo "  Transaction Limit: 100,000,000 kopecks (1,000,000 RUB = 1M RUB)"
    echo "  Minimum Amount: 1,000,000 kopecks (10,000 RUB)"
else
    echo "❌ Team registration failed: $TEAM_RESPONSE"
    exit 1
fi
echo ""

# Wait a moment for the team to be fully set up
sleep 2

# Step 2: Test payments within limits
echo "--- Step 2: Testing payments within limits ---"

# First payment (2M RUB = 200,000,000 kopecks)
PAYMENT1_ID=$(init_payment 200000000 "order-within-1" "true")

# Second payment (1.5M RUB = 150,000,000 kopecks) - total would be 3.5M RUB, within 5M limit
PAYMENT2_ID=$(init_payment 150000000 "order-within-2" "true")

# Step 3: Test payments exceeding limits
echo "--- Step 3: Testing payments exceeding limits ---"

# Payment exceeding transaction limit (1.2M RUB = 120,000,000 kopecks, exceeds 1M limit)
init_payment 120000000 "order-exceed-transaction" "false"

# Payment that would exceed daily limit (2M RUB = 200,000,000 kopecks)
# Current daily total: 3.5M RUB, adding 2M would make 5.5M RUB, exceeding 5M limit
init_payment 200000000 "order-exceed-daily" "false"

# Step 4: Update team limits (if API exists)
echo "--- Step 4: Attempting to update team limits ---"

UPDATE_RESPONSE=$(curl -s -X PUT "$BASE_URL/api/v1/TeamRegistration/update/$TEAM_SLUG" \
    -H "Content-Type: application/json" \
    -H "X-Admin-Token: $ADMIN_TOKEN" \
    -d "{
        \"teamName\": \"Daily Limits Test Team (Updated)\",
        \"dailyPaymentLimit\": 300000000,
        \"maxPaymentAmount\": 800000000
    }" 2>/dev/null || echo '{"success": false}')

UPDATE_SUCCESS=$(echo "$UPDATE_RESPONSE" | jq -r '.success // false')

if [ "$UPDATE_SUCCESS" = "true" ]; then
    echo "✓ Team limits updated successfully"
    echo "  New Daily Limit: 300,000,000 kopecks (3,000,000 RUB = 3M RUB)"
    echo "  New Transaction Limit: 80,000,000 kopecks (800,000 RUB = 800K RUB)"
    
    # Wait for the update to take effect
    sleep 2
    
    # Step 5: Test with new limits
    echo ""
    echo "--- Step 5: Testing with updated limits ---"
    
    # Payment within new limits (600K RUB = 60,000,000 kopecks) - should succeed
    init_payment 60000000 "order-new-limits-within" "true"
    
    # Payment exceeding new transaction limit (900K RUB = 90,000,000 kopecks) - should fail
    init_payment 90000000 "order-new-limits-exceed" "false"
    
else
    echo "⚠️  Team limits update API not available or failed"
    echo "Response: $UPDATE_RESPONSE"
fi

# Step 6: Test getting team information
echo ""
echo "--- Step 6: Checking team configuration ---"

TEAM_INFO=$(curl -s -X GET "$BASE_URL/api/v1/TeamRegistration/info/$TEAM_SLUG" \
    -H "X-Admin-Token: $ADMIN_TOKEN" 2>/dev/null || echo '{"success": false}')
TEAM_INFO_SUCCESS=$(echo "$TEAM_INFO" | jq -r '.success // false')

if [ "$TEAM_INFO_SUCCESS" = "true" ]; then
    echo "✓ Team information retrieved:"
    echo "$TEAM_INFO" | jq '.'
else
    echo "⚠️  Could not retrieve team information"
fi

echo ""
echo "=== Test Summary ==="
echo "Team Slug: $TEAM_SLUG"
echo "Team ID: $TEAM_ID"
echo "Test completed. Check the logs above for detailed results."
echo ""
echo "To clean up, you may want to deactivate the test team:"
echo "curl -X DELETE '$BASE_URL/api/v1/TeamRegistration/team/$TEAM_SLUG'"
echo ""
#!/bin/bash

# Debug script to test token generation with raw password approach
TEST_TEAM_SLUG="demo-team"
RAW_PASSWORD="demo123"

echo "=== Token Generation Debug ==="
echo "Team Slug: $TEST_TEAM_SLUG"
echo "Raw Password: $RAW_PASSWORD"
echo ""

# Test simple token for basic operation (just teamSlug + Password)
echo "=== Simple Token Test ==="
simple_params="Password${RAW_PASSWORD}teamSlug${TEST_TEAM_SLUG}"
echo "Concatenated: $simple_params"
simple_token=$(echo -n "$simple_params" | sha256sum | cut -d' ' -f1)
echo "Generated Token: $simple_token"
echo ""

# Test what the server might expect for payment init
echo "=== Payment Init Token Test (All Parameters) ==="

# All scalar parameters from the request in alphabetical order (excluding Token itself)
# These match the PaymentInitRequestDto + BaseRequestDto properties

# Using the actual values from our test request
amount="250000"
correlationId=""                # Optional, let's leave empty
currency="RUB"
customerKey="customer-test-123" 
description="Test payment for order order-20250807111455"
descriptor=""                   # Optional
email="customer@example.com"
failURL="https://merchant.example.com/fail"
language="ru"
notificationURL="https://merchant.example.com/webhook"
orderId="order-20250807111455"
payType="O"
paymentExpiry="30"
phone="+79991234567"
redirectMethod="POST"
successURL="https://merchant.example.com/success"
teamSlug="$TEST_TEAM_SLUG"
timestamp=""                    # Will be set by server, might be empty in our case
version="1.0"

# Build parameters dict in alphabetical order (like server does)
# NOTE: Only include non-empty values as that's likely what the server does

echo "Building token with all parameters:"
init_params=""

# Add each parameter in alphabetical order (only non-empty values)
if [ -n "$amount" ]; then 
    echo "  Amount: $amount"
    init_params+="$amount"
fi

if [ -n "$correlationId" ]; then
    echo "  CorrelationId: $correlationId"  
    init_params+="$correlationId"
fi

if [ -n "$currency" ]; then
    echo "  Currency: $currency"
    init_params+="$currency"
fi

if [ -n "$customerKey" ]; then
    echo "  CustomerKey: $customerKey"
    init_params+="$customerKey"
fi

if [ -n "$description" ]; then
    echo "  Description: $description"
    init_params+="$description"
fi

if [ -n "$descriptor" ]; then
    echo "  Descriptor: $descriptor"
    init_params+="$descriptor"
fi

if [ -n "$email" ]; then
    echo "  Email: $email"
    init_params+="$email"
fi

if [ -n "$failURL" ]; then
    echo "  FailURL: $failURL"
    init_params+="$failURL"
fi

if [ -n "$language" ]; then
    echo "  Language: $language"
    init_params+="$language"
fi

if [ -n "$notificationURL" ]; then
    echo "  NotificationURL: $notificationURL"
    init_params+="$notificationURL"
fi

if [ -n "$orderId" ]; then
    echo "  OrderId: $orderId"
    init_params+="$orderId"
fi

# Add Password (this is the key part - server adds this)
echo "  Password: $RAW_PASSWORD"
init_params+="$RAW_PASSWORD"

if [ -n "$payType" ]; then
    echo "  PayType: $payType"
    init_params+="$payType"
fi

if [ -n "$paymentExpiry" ]; then
    echo "  PaymentExpiry: $paymentExpiry"
    init_params+="$paymentExpiry"
fi

if [ -n "$phone" ]; then
    echo "  Phone: $phone"
    init_params+="$phone"
fi

if [ -n "$redirectMethod" ]; then
    echo "  RedirectMethod: $redirectMethod"
    init_params+="$redirectMethod"
fi

if [ -n "$successURL" ]; then
    echo "  SuccessURL: $successURL"
    init_params+="$successURL"
fi

if [ -n "$teamSlug" ]; then
    echo "  TeamSlug: $teamSlug"
    init_params+="$teamSlug"
fi

if [ -n "$timestamp" ]; then
    echo "  Timestamp: $timestamp"
    init_params+="$timestamp"
fi

if [ -n "$version" ]; then
    echo "  Version: $version"
    init_params+="$version"
fi

echo ""
echo "Final concatenated string:"
echo "$init_params"
echo ""

init_token=$(echo -n "$init_params" | sha256sum | cut -d' ' -f1)
echo "Generated Token: $init_token"
echo ""

# Make a simple API call to test
echo "=== API Test ==="
curl -s -X POST http://localhost:5162/api/v1/PaymentInit/init \
  -H "Content-Type: application/json" \
  -d "{
    \"teamSlug\": \"$TEST_TEAM_SLUG\",
    \"token\": \"$init_token\",
    \"amount\": 250000,
    \"orderId\": \"order-20250807111455\",
    \"currency\": \"RUB\",
    \"payType\": \"O\",
    \"description\": \"Test payment for order order-20250807111455\",
    \"customerKey\": \"customer-test-123\",
    \"email\": \"customer@example.com\",
    \"phone\": \"+79991234567\",
    \"language\": \"ru\",
    \"paymentExpiry\": 30,
    \"successURL\": \"https://merchant.example.com/success\",
    \"failURL\": \"https://merchant.example.com/fail\",
    \"notificationURL\": \"https://merchant.example.com/webhook\",
    \"items\": [
        {
            \"name\": \"Test Product\",
            \"quantity\": 1,
            \"price\": 250000,
            \"amount\": 250000,
            \"tax\": \"vat20\"
        }
    ]
}" | jq '.'
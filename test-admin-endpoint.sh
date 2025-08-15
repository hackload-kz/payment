#!/bin/bash

# Test script for the admin clear-database endpoint

# Configuration
BASE_URL="http://localhost:5162"
ADMIN_TOKEN="admin_token_2025_hackload_payment_gateway_secure_key_dev_only"  # Matches AdminAuthentication configuration

echo "Testing Admin Clear Database Endpoint"
echo "======================================"

# Function to make HTTP requests with error handling
make_request() {
    local method=$1
    local url=$2
    local headers=$3
    local data=$4
    
    echo "Making $method request to: $url"
    
    if [[ -n "$data" ]]; then
        curl -s -X "$method" \
             -H "Content-Type: application/json" \
             $headers \
             -d "$data" \
             "$url" | jq '.'
    else
        curl -s -X "$method" \
             $headers \
             "$url" | jq '.'
    fi
}

echo
echo "1. Testing admin status endpoint (no auth required)"
echo "---------------------------------------------------"
make_request "GET" "$BASE_URL/api/v1/Admin/status" ""

echo
echo "2. Testing clear-database endpoint without authentication"
echo "--------------------------------------------------------"
make_request "POST" "$BASE_URL/api/v1/Admin/clear-database" ""

echo
echo "3. Testing clear-database endpoint with invalid token"
echo "----------------------------------------------------"
make_request "POST" "$BASE_URL/api/v1/Admin/clear-database" "-H 'Authorization: Bearer invalid-token'" ""

echo
echo "4. Testing clear-database endpoint with valid Bearer token"
echo "--------------------------------------------------------"
make_request "POST" "$BASE_URL/api/v1/Admin/clear-database" "-H 'Authorization: Bearer $ADMIN_TOKEN'" ""

echo
echo "5. Testing clear-database endpoint with valid X-Admin-Token header"
echo "-----------------------------------------------------------------"
make_request "POST" "$BASE_URL/api/v1/Admin/clear-database" "-H 'X-Admin-Token: $ADMIN_TOKEN'" ""

echo
echo "Test completed!"
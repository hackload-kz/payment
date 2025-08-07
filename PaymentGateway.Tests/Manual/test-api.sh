#!/bin/bash

# PaymentGateway API Test Script
# Make sure your PaymentGateway app is running before running this script

BASE_URL="http://localhost:5162"
ADMIN_TOKEN="admin_token_2025_hackload_payment_gateway_secure_key_dev_only"
TEAM_SLUG="test-$(date +%Y%m%d-%H%M%S)"

echo "🚀 Testing PaymentGateway API"
echo "Base URL: $BASE_URL"
echo "Team Slug: $TEAM_SLUG"
echo "==========================================="

# Test 1: Check availability (should be available)
echo ""
echo "📋 Test 1: Check slug availability (new slug)"
curl -k -H "X-Admin-Token: $ADMIN_TOKEN" \
     -H "Accept: application/json" \
     "$BASE_URL/api/v1/TeamRegistration/check-availability/$TEAM_SLUG" \
     -w "\n\n"

# Test 2: Register new team
echo ""
echo "📋 Test 2: Register new team"
curl -k -X POST \
     -H "X-Admin-Token: $ADMIN_TOKEN" \
     -H "Content-Type: application/json" \
     -H "Accept: application/json" \
     -d '{
       "teamSlug": "'$TEAM_SLUG'",
       "teamName": "Test Team '$TEAM_SLUG'",
       "password": "SecurePassword123!",
       "email": "'$TEAM_SLUG'@example.com",
       "phone": "+1234567890",
       "successURL": "https://'$TEAM_SLUG'.example.com/success",
       "failURL": "https://'$TEAM_SLUG'.example.com/fail",
       "notificationURL": "https://'$TEAM_SLUG'.example.com/webhook",
       "supportedCurrencies": "RUB,USD,EUR",
       "businessInfo": {
         "businessType": "ecommerce",
         "description": "Test business"
       },
       "acceptTerms": true
     }' \
     "$BASE_URL/api/v1/TeamRegistration/register" \
     -w "\n\n"

# Test 3: Check availability (should now be taken)
echo ""
echo "📋 Test 3: Check slug availability (existing slug)"
curl -k -H "X-Admin-Token: $ADMIN_TOKEN" \
     -H "Accept: application/json" \
     "$BASE_URL/api/v1/TeamRegistration/check-availability/$TEAM_SLUG" \
     -w "\n\n"

# Test 4: Get team status
echo ""
echo "📋 Test 4: Get team status"
curl -k -H "X-Admin-Token: $ADMIN_TOKEN" \
     -H "Accept: application/json" \
     "$BASE_URL/api/v1/TeamRegistration/status/$TEAM_SLUG" \
     -w "\n\n"

# Test 5: Try to register duplicate (should fail with 409)
echo ""
echo "📋 Test 5: Try to register duplicate team (should fail)"
curl -k -X POST \
     -H "X-Admin-Token: $ADMIN_TOKEN" \
     -H "Content-Type: application/json" \
     -H "Accept: application/json" \
     -d '{
       "teamSlug": "'$TEAM_SLUG'",
       "teamName": "Duplicate Team",
       "password": "AnotherPassword123!",
       "email": "different@example.com",
       "phone": "+0987654321",
       "successURL": "https://different.example.com/success",
       "failURL": "https://different.example.com/fail",
       "supportedCurrencies": "USD",
       "acceptTerms": true
     }' \
     "$BASE_URL/api/v1/TeamRegistration/register" \
     -w "\n\n"

echo ""
echo "🎯 Test completed!"
echo "Check the responses above to verify everything works correctly."
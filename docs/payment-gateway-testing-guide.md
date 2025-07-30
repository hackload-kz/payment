# PaymentGateway.API Comprehensive Testing Guide

## Overview

This document provides comprehensive external testing scenarios for the PaymentGateway.API service based on the payment lifecycle specification and available API endpoints. The testing covers all payment states and transitions outlined in `specs/payment-lifecycle.md`.

## API Endpoints Overview

The PaymentGateway.API exposes the following REST endpoints:

- **Payment Initialization**: `POST /api/v1/paymentinit/init`
- **Payment Form**: `GET /api/v1/paymentform/render/{paymentId}`
- **Form Submission**: `POST /api/v1/paymentform/submit`
- **Payment Confirmation**: `POST /api/v1/paymentconfirm/confirm/{paymentId}`
- **Payment Cancellation**: `POST /api/v1/paymentcancel/cancel/{paymentId}`
- **Payment Status Check**: `GET /api/v1/paymentcheck/status/{paymentId}`
- **Metrics**: `GET /api/v1/metrics`

## Test Environment Setup

```bash
# Base URL for testing
BASE_URL="http://localhost:5162"

# Required headers for authentication
TEAM_SLUG="test-merchant"
AUTH_TOKEN="your-hmac-signed-token"
```

## 1. Payment Lifecycle Testing Scenarios

### 1.1 Happy Path - Complete Payment Flow

#### Test Case: INIT → NEW → AUTHORIZED → CONFIRMED

**Step 1: Initialize Payment**
```bash
curl -X POST "${BASE_URL}/api/v1/paymentinit/init" \
  -H "Content-Type: application/json" \
  -d '{
    "teamSlug": "test-merchant",
    "token": "signed-token-here",
    "amount": 150000,
    "orderId": "order-12345",
    "currency": "RUB",
    "description": "Test payment",
    "successURL": "https://merchant.com/success",
    "failURL": "https://merchant.com/fail",
    "notificationURL": "https://merchant.com/webhook",
    "paymentExpiry": 30,
    "email": "test@example.com",
    "language": "en"
  }'
```

**Expected Response:**
```json
{
  "success": true,
  "paymentId": "pay_123456789",
  "orderId": "order-12345",
  "status": "NEW",
  "amount": 150000,
  "currency": "RUB",
  "paymentURL": "http://localhost:5162/api/v1/paymentform/render/pay_123456789",
  "expiresAt": "2025-01-30T12:30:00Z",
  "createdAt": "2025-01-30T12:00:00Z"
}
```

**Step 2: Render Payment Form**
```bash
curl -X GET "${BASE_URL}/api/v1/paymentform/render/pay_123456789?lang=en" \
  -H "Accept: text/html"
```

**Expected Response:** HTML payment form with card input fields

**Step 3: Submit Payment Form**
```bash
curl -X POST "${BASE_URL}/api/v1/paymentform/submit" \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "paymentId=pay_123456789&cardNumber=4111111111111111&expiryDate=12/25&cvv=123&cardholderName=John Doe&email=test@example.com&csrfToken=csrf-token-here"
```

**Expected Response:** HTML success page or redirect to success URL

**Step 4: Confirm Payment**
```bash
curl -X POST "${BASE_URL}/api/v1/paymentconfirm/confirm/pay_123456789" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer ${AUTH_TOKEN}"
```

**Expected Response:**
```json
{
  "success": true,
  "paymentId": "pay_123456789",
  "status": "CONFIRMED",
  "message": "Payment confirmed successfully"
}
```

### 1.2 Payment Cancellation Flow

#### Test Case: NEW → CANCELLED

**Step 1: Initialize Payment** (same as above)

**Step 2: Cancel Payment**
```bash
curl -X POST "${BASE_URL}/api/v1/paymentcancel/cancel/pay_123456789" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer ${AUTH_TOKEN}" \
  -d '{
    "reason": "Customer requested cancellation",
    "cancelledBy": "merchant"
  }'
```

**Expected Response:**
```json
{
  "success": true,
  "paymentId": "pay_123456789",
  "status": "CANCELLED",
  "message": "Payment cancelled successfully"
}
```

### 1.3 Payment Expiration Flow

#### Test Case: NEW → DEADLINE_EXPIRED

**Step 1: Initialize Payment with short expiry**
```bash
curl -X POST "${BASE_URL}/api/v1/paymentinit/init" \
  -H "Content-Type: application/json" \
  -d '{
    "teamSlug": "test-merchant",
    "token": "signed-token-here",
    "amount": 150000,
    "orderId": "order-expiry-test",
    "currency": "RUB",
    "description": "Expiry test payment",
    "paymentExpiry": 1,
    "successURL": "https://merchant.com/success",
    "failURL": "https://merchant.com/fail"
  }'
```

**Step 2: Wait for expiration (>1 minute)**

**Step 3: Check payment status**
```bash
curl -X GET "${BASE_URL}/api/v1/paymentcheck/status/pay_expiry_test" \
  -H "Authorization: Bearer ${AUTH_TOKEN}"
```

**Expected Response:**
```json
{
  "paymentId": "pay_expiry_test",
  "status": "DEADLINE_EXPIRED",
  "message": "Payment expired"
}
```

### 1.4 Authorization Failure Flow

#### Test Case: NEW → AUTHORIZING → AUTH_FAIL

**Step 1: Initialize Payment** (standard initialization)

**Step 2: Submit Form with Failed Card**
```bash
curl -X POST "${BASE_URL}/api/v1/paymentform/submit" \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "paymentId=pay_123456789&cardNumber=4000000000000002&expiryDate=12/25&cvv=123&cardholderName=John Doe&email=test@example.com&csrfToken=csrf-token-here"
```

**Expected Response:** HTML failure page with error message

## 2. Authentication and Authorization Testing

### 2.1 Valid Authentication

```bash
# Test with valid team credentials
curl -X POST "${BASE_URL}/api/v1/paymentinit/init" \
  -H "Content-Type: application/json" \
  -d '{
    "teamSlug": "valid-merchant",
    "token": "valid-signed-token",
    "amount": 100000,
    "currency": "RUB",
    "orderId": "auth-test-001"
  }'
```

**Expected:** HTTP 200 with successful payment initialization

### 2.2 Invalid Team Slug

```bash
curl -X POST "${BASE_URL}/api/v1/paymentinit/init" \
  -H "Content-Type: application/json" \
  -d '{
    "teamSlug": "invalid-merchant",
    "token": "some-token",
    "amount": 100000,
    "currency": "RUB",
    "orderId": "auth-test-002"
  }'
```

**Expected:** HTTP 401 Unauthorized

### 2.3 Invalid Token

```bash
curl -X POST "${BASE_URL}/api/v1/paymentinit/init" \
  -H "Content-Type: application/json" \
  -d '{
    "teamSlug": "valid-merchant",
    "token": "invalid-token",
    "amount": 100000,
    "currency": "RUB",
    "orderId": "auth-test-003"
  }'
```

**Expected:** HTTP 401 Unauthorized

### 2.4 Missing Authentication

```bash
curl -X POST "${BASE_URL}/api/v1/paymentinit/init" \
  -H "Content-Type: application/json" \
  -d '{
    "amount": 100000,
    "currency": "RUB",
    "orderId": "auth-test-004"
  }'
```

**Expected:** HTTP 400 Bad Request with validation errors

## 3. Input Validation Testing

### 3.1 Amount Validation

#### Test Case: Negative Amount
```bash
curl -X POST "${BASE_URL}/api/v1/paymentinit/init" \
  -H "Content-Type: application/json" \
  -d '{
    "teamSlug": "test-merchant",
    "token": "valid-token",
    "amount": -1000,
    "currency": "RUB",
    "orderId": "validation-001"
  }'
```
**Expected:** HTTP 400 with validation error

#### Test Case: Zero Amount
```bash
curl -X POST "${BASE_URL}/api/v1/paymentinit/init" \
  -H "Content-Type: application/json" \
  -d '{
    "teamSlug": "test-merchant",
    "token": "valid-token",
    "amount": 0,
    "currency": "RUB",
    "orderId": "validation-002"
  }'
```
**Expected:** HTTP 400 with validation error

#### Test Case: Excessive Amount
```bash
curl -X POST "${BASE_URL}/api/v1/paymentinit/init" \
  -H "Content-Type: application/json" \
  -d '{
    "teamSlug": "test-merchant",
    "token": "valid-token",
    "amount": 100000001,
    "currency": "RUB",
    "orderId": "validation-003"
  }'
```
**Expected:** HTTP 400 with validation error

### 3.2 Currency Validation

#### Test Case: Invalid Currency
```bash
curl -X POST "${BASE_URL}/api/v1/paymentinit/init" \
  -H "Content-Type: application/json" \
  -d '{
    "teamSlug": "test-merchant",
    "token": "valid-token",
    "amount": 100000,
    "currency": "JPY",
    "orderId": "validation-004"
  }'
```
**Expected:** HTTP 400 with currency validation error

### 3.3 URL Validation

#### Test Case: Invalid Success URL
```bash
curl -X POST "${BASE_URL}/api/v1/paymentinit/init" \
  -H "Content-Type: application/json" \
  -d '{
    "teamSlug": "test-merchant",
    "token": "valid-token",
    "amount": 100000,
    "currency": "RUB",
    "orderId": "validation-005",
    "successURL": "not-a-valid-url"
  }'
```
**Expected:** HTTP 400 with URL validation error

### 3.4 Email Validation

#### Test Case: Invalid Email Format
```bash
curl -X POST "${BASE_URL}/api/v1/paymentinit/init" \
  -H "Content-Type: application/json" \
  -d '{
    "teamSlug": "test-merchant",
    "token": "valid-token",
    "amount": 100000,
    "currency": "RUB",
    "orderId": "validation-006",
    "email": "invalid-email-format"
  }'
```
**Expected:** HTTP 400 with email validation error

## 4. Card Data Validation Testing

### 4.1 Card Number Validation

#### Test Case: Invalid Card Number
```bash
curl -X POST "${BASE_URL}/api/v1/paymentform/submit" \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "paymentId=pay_123456789&cardNumber=1234567890123456&expiryDate=12/25&cvv=123&cardholderName=John Doe&email=test@example.com&csrfToken=csrf-token"
```
**Expected:** Form validation error for invalid card number

#### Test Case: Expired Card
```bash
curl -X POST "${BASE_URL}/api/v1/paymentform/submit" \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "paymentId=pay_123456789&cardNumber=4111111111111111&expiryDate=01/20&cvv=123&cardholderName=John Doe&email=test@example.com&csrfToken=csrf-token"
```
**Expected:** Form validation error for expired card

#### Test Case: Invalid CVV
```bash
curl -X POST "${BASE_URL}/api/v1/paymentform/submit" \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "paymentId=pay_123456789&cardNumber=4111111111111111&expiryDate=12/25&cvv=12&cardholderName=John Doe&email=test@example.com&csrfToken=csrf-token"
```
**Expected:** Form validation error for invalid CVV

## 5. Security Testing

### 5.1 CSRF Protection

#### Test Case: Missing CSRF Token
```bash
curl -X POST "${BASE_URL}/api/v1/paymentform/submit" \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "paymentId=pay_123456789&cardNumber=4111111111111111&expiryDate=12/25&cvv=123&cardholderName=John Doe&email=test@example.com"
```
**Expected:** HTTP 400 with CSRF validation error

#### Test Case: Invalid CSRF Token
```bash
curl -X POST "${BASE_URL}/api/v1/paymentform/submit" \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "paymentId=pay_123456789&cardNumber=4111111111111111&expiryDate=12/25&cvv=123&cardholderName=John Doe&email=test@example.com&csrfToken=invalid-token"
```
**Expected:** HTTP 400 with CSRF validation error

### 5.2 Rate Limiting

#### Test Case: Excessive Requests
```bash
# Send multiple rapid requests
for i in {1..101}; do
  curl -X POST "${BASE_URL}/api/v1/paymentinit/init" \
    -H "Content-Type: application/json" \
    -d "{\"teamSlug\":\"test-merchant\",\"token\":\"valid-token\",\"amount\":100000,\"currency\":\"RUB\",\"orderId\":\"rate-test-$i\"}" &
done
wait
```
**Expected:** HTTP 429 Too Many Requests after rate limit exceeded

## 6. Error Handling Testing

### 6.1 Invalid Payment ID

```bash
curl -X GET "${BASE_URL}/api/v1/paymentcheck/status/invalid-payment-id" \
  -H "Authorization: Bearer ${AUTH_TOKEN}"
```
**Expected:** HTTP 404 Not Found

### 6.2 Payment State Conflicts

#### Test Case: Double Confirmation
```bash
# First confirmation
curl -X POST "${BASE_URL}/api/v1/paymentconfirm/confirm/pay_123456789" \
  -H "Authorization: Bearer ${AUTH_TOKEN}"

# Second confirmation attempt
curl -X POST "${BASE_URL}/api/v1/paymentconfirm/confirm/pay_123456789" \
  -H "Authorization: Bearer ${AUTH_TOKEN}"
```
**Expected:** Second request should return HTTP 400 with state conflict error

### 6.3 Malformed JSON

```bash
curl -X POST "${BASE_URL}/api/v1/paymentinit/init" \
  -H "Content-Type: application/json" \
  -d '{"teamSlug":"test-merchant","amount":invalid}'
```
**Expected:** HTTP 400 with JSON parsing error

## 7. Performance and Load Testing

### 7.1 Concurrent Payment Initialization

```bash
# Test concurrent payment creation
for i in {1..50}; do
  curl -X POST "${BASE_URL}/api/v1/paymentinit/init" \
    -H "Content-Type: application/json" \
    -d "{\"teamSlug\":\"test-merchant\",\"token\":\"valid-token\",\"amount\":100000,\"currency\":\"RUB\",\"orderId\":\"perf-test-$i\"}" &
done
wait
```

### 7.2 Payment Form Load Testing

```bash
# Test payment form rendering under load
PAYMENT_ID="pay_123456789"
for i in {1..100}; do
  curl -X GET "${BASE_URL}/api/v1/paymentform/render/${PAYMENT_ID}" &
done
wait
```

## 8. Integration Testing

### 8.1 Full Payment Workflow with Items

```bash
curl -X POST "${BASE_URL}/api/v1/paymentinit/init" \
  -H "Content-Type: application/json" \
  -d '{
    "teamSlug": "test-merchant",
    "token": "valid-token",
    "amount": 250000,
    "currency": "RUB",
    "orderId": "integration-001",
    "description": "Multi-item purchase",
    "items": [
      {
        "name": "Product A",
        "quantity": 2,
        "price": 75000,
        "amount": 150000
      },
      {
        "name": "Product B",
        "quantity": 1,
        "price": 100000,
        "amount": 100000
      }
    ],
    "successURL": "https://merchant.com/success",
    "failURL": "https://merchant.com/fail",
    "notificationURL": "https://merchant.com/webhook"
  }'
```

### 8.2 Webhook Testing

Set up a test webhook endpoint and verify notifications are sent for:
- Payment authorization
- Payment confirmation
- Payment cancellation
- Payment failure

## 9. Metrics and Monitoring Testing

### 9.1 Metrics Endpoint

```bash
curl -X GET "${BASE_URL}/api/v1/metrics" \
  -H "Accept: text/plain"
```
**Expected:** Prometheus-formatted metrics

### 9.2 Payment Metrics by Team

```bash
curl -X GET "${BASE_URL}/api/v1/paymentinit/metrics/test-merchant" \
  -H "Authorization: Bearer ${AUTH_TOKEN}"
```
**Expected:** JSON with team-specific payment metrics

## 10. Negative Testing Scenarios

### 10.1 SQL Injection Attempts

```bash
curl -X POST "${BASE_URL}/api/v1/paymentinit/init" \
  -H "Content-Type: application/json" \
  -d '{
    "teamSlug": "test\"; DROP TABLE payments; --",
    "token": "valid-token",
    "amount": 100000,
    "currency": "RUB",
    "orderId": "sql-injection-test"
  }'
```
**Expected:** Safe handling without SQL injection

### 10.2 XSS Attempts

```bash
curl -X POST "${BASE_URL}/api/v1/paymentinit/init" \
  -H "Content-Type: application/json" \
  -d '{
    "teamSlug": "test-merchant",
    "token": "valid-token",
    "amount": 100000,
    "currency": "RUB",
    "orderId": "xss-test",
    "description": "<script>alert(\"xss\")</script>"
  }'
```
**Expected:** Description should be sanitized in HTML output

## Test Automation

### Example Test Script

```bash
#!/bin/bash

BASE_URL="http://localhost:5162"
TEAM_SLUG="test-merchant"
AUTH_TOKEN="your-test-token"

# Function to run test and check response
run_test() {
    local test_name="$1"
    local expected_status="$2"
    shift 2
    
    echo "Running test: $test_name"
    response=$(curl -s -w "%{http_code}" "$@")
    status="${response: -3}"
    
    if [ "$status" = "$expected_status" ]; then
        echo "✅ PASS: $test_name (HTTP $status)"
    else
        echo "❌ FAIL: $test_name (Expected HTTP $expected_status, got HTTP $status)"
    fi
    echo "Response: ${response%???}"
    echo "---"
}

# Test Suite
run_test "Valid Payment Initialization" "200" \
    -X POST "${BASE_URL}/api/v1/paymentinit/init" \
    -H "Content-Type: application/json" \
    -d '{"teamSlug":"'$TEAM_SLUG'","token":"'$AUTH_TOKEN'","amount":100000,"currency":"RUB","orderId":"test-001"}'

run_test "Invalid Amount" "400" \
    -X POST "${BASE_URL}/api/v1/paymentinit/init" \
    -H "Content-Type: application/json" \
    -d '{"teamSlug":"'$TEAM_SLUG'","token":"'$AUTH_TOKEN'","amount":-1000,"currency":"RUB","orderId":"test-002"}'

# Add more tests...
```

## Conclusion

This comprehensive testing guide covers all major aspects of the PaymentGateway.API service:

- **Payment Lifecycle Testing**: All states and transitions from the payment lifecycle specification
- **Authentication & Authorization**: Team-based security validation
- **Input Validation**: Comprehensive data validation testing
- **Security Testing**: CSRF, rate limiting, and injection protection
- **Error Handling**: Various error scenarios and edge cases
- **Performance Testing**: Load and concurrent access scenarios
- **Integration Testing**: End-to-end workflow validation

Execute these tests systematically to ensure the payment gateway operates correctly under all conditions and properly handles both valid and invalid scenarios.
# PaymentGateway API Reference

Complete API reference with curl examples for testing the PaymentGateway service.

## Base Configuration

### API Base URLs
- **Development**: `https://localhost:7162` or `http://localhost:5162`
- **Production**: Configure according to your deployment

### Authentication

The PaymentGateway API uses **SHA-256 HMAC Token Authentication**:

- **TeamSlug**: Team identifier from database
- **Token**: SHA-256 hash of sorted request parameters + password
- **Authentication**: Done via `PaymentAuthenticationService` and middleware

```bash
# Configuration for examples
BASE_URL="https://localhost:7162"

# Alternative for HTTP
BASE_URL="http://localhost:5162"
```

## Test Data Setup

The PaymentGateway includes seeded test data in the database:

### Available Test Teams

| TeamSlug | Password | Description |
|----------|----------|-------------|
| `demo-team` | `demo123` | Demo team with webhook URLs configured |
| `test-team` | `test123` | Test team for basic testing |

### Token Generation

To authenticate, you need to generate a SHA-256 token:

1. **Collect request parameters** (excluding the Token itself)
2. **Add the password** (`Password: <hashed_password>`)
3. **Sort parameters alphabetically** by key
4. **Concatenate values** in sorted order
5. **Generate SHA-256 hash** of the concatenated string

**Example Token Generation (Python)**:
```python
import hashlib
import json

def generate_token(team_slug, password_hash, request_params):
    # Step 1: Collect parameters (exclude Token)
    token_params = {k: str(v) for k, v in request_params.items() if k.lower() != 'token'}
    
    # Step 2: Add password
    token_params['Password'] = password_hash
    
    # Step 3: Sort alphabetically
    sorted_keys = sorted(token_params.keys())
    
    # Step 4: Concatenate values
    concatenated = ''.join(token_params[key] for key in sorted_keys)
    
    # Step 5: Generate SHA-256 hash
    return hashlib.sha256(concatenated.encode('utf-8')).hexdigest().lower()

# Example usage
request_data = {
    "TeamSlug": "demo-team",
    "Amount": 150000,
    "OrderId": "ORDER_12345",
    "Currency": "RUB"
}

# Password hash for demo-team (demo123)
password_hash = "d3ad9315b7be5dd53b31a273b3b3aba5defe700808305aa16a3062b76658a791"

token = generate_token("demo-team", password_hash, request_data)
request_data["Token"] = token
```

**Test Team Password Hashes**:
- `demo-team`: `d3ad9315b7be5dd53b31a273b3b3aba5defe700808305aa16a3062b76658a791`
- `test-team`: `ecd71870d1963316a97e3ac3408c9835ad8cf0f3c1bc703527c30265534f75ae`

## Team Registration API Endpoints

**⚠️ Admin Authentication Required**: All team registration endpoints require admin authentication via the `X-Admin-Token` header.

```bash
# Add this header to all team registration requests
-H "X-Admin-Token: admin_token_2025_hackload_payment_gateway_secure_key_dev_only"
```

### 1. Register Team

Creates a new team/merchant account for payment processing.

**Endpoint**: `POST /api/v1/TeamRegistration/register`

```bash
curl -X POST "${BASE_URL}/api/v1/TeamRegistration/register" \
  -H "Content-Type: application/json" \
  -H "X-Admin-Token: admin_token_2025_hackload_payment_gateway_secure_key_dev_only" \
  -d '{
    "teamSlug": "my-online-store",
    "password": "SecurePassword123!",
    "teamName": "My Online Store",
    "email": "merchant@mystore.com",
    "phone": "+1234567890",
    "successURL": "https://mystore.com/payment/success",
    "failURL": "https://mystore.com/payment/fail",
    "notificationURL": "https://mystore.com/payment/webhook",
    "supportedCurrencies": "RUB,USD,EUR",
    "businessInfo": {
      "businessType": "ecommerce",
      "website": "https://mystore.com"
    },
    "acceptTerms": true
  }'
```

**Response**:
```json
{
  "success": true,
  "message": "Team registered successfully",
  "teamSlug": "my-online-store",
  "teamId": "123e4567-e89b-12d3-a456-426614174000",
  "passwordHashPreview": "d3ad9315...",
  "createdAt": "2025-08-06T10:30:00Z",
  "status": "ACTIVE",
  "apiEndpoint": "https://gateway.hackload.com/api/v1",
  "details": {
    "teamName": "My Online Store",
    "email": "merchant@mystore.com",
    "supportedCurrencies": ["RUB", "USD", "EUR"],
    "nextSteps": [
      "Test payment initialization using your credentials",
      "Configure webhook endpoint for notifications",
      "Review API documentation for integration"
    ]
  }
}
```

### 2. Check Team Slug Availability

Checks if a team slug is available for registration.

**Endpoint**: `GET /api/v1/TeamRegistration/check-availability/{teamSlug}`

```bash
curl -X GET "${BASE_URL}/api/v1/TeamRegistration/check-availability/my-store" \
  -H "X-Admin-Token: admin_token_2025_hackload_payment_gateway_secure_key_dev_only"
```

**Response**:
```json
{
  "teamSlug": "my-store",
  "available": true,
  "reason": "Available"
}
```

### 3. Get Team Status

Retrieves team registration status and details.

**Endpoint**: `GET /api/v1/TeamRegistration/status/{teamSlug}`

```bash
curl -X GET "${BASE_URL}/api/v1/TeamRegistration/status/my-store" \
  -H "X-Admin-Token: admin_token_2025_hackload_payment_gateway_secure_key_dev_only"
```

**Response**:
```json
{
  "teamSlug": "my-store",
  "teamName": "My Store",
  "status": "ACTIVE",
  "createdAt": "2025-08-06T10:30:00Z",
  "email": "merchant@mystore.com",
  "supportedCurrencies": ["RUB", "USD", "EUR"]
}
```

## Administrative API Endpoints

**⚠️ Admin Authentication Required**: All administrative endpoints require admin authentication via one of these methods:
- **Authorization header**: `Authorization: Bearer {admin-token}`
- **Custom header**: `X-Admin-Token: {admin-token}`

The admin token is configured in `AdminAuthentication.AdminToken` in application settings.

```bash
# Add one of these headers to all admin requests
-H "Authorization: Bearer admin_token_2025_hackload_payment_gateway_secure_key_dev_only"
# OR
-H "X-Admin-Token: admin_token_2025_hackload_payment_gateway_secure_key_dev_only"
```

### 1. Admin Status Check

Check admin service status and configuration.

**Endpoint**: `GET /api/v1/Admin/status`

```bash
curl -X GET "${BASE_URL}/api/v1/Admin/status" \
  -H "Accept: application/json"
```

**Response**:
```json
{
  "adminTokenConfigured": true,
  "serviceVersion": "1.0",
  "serverTime": "2025-08-15T10:30:00Z"
}
```

### 2. Clear Database

**⚠️ DESTRUCTIVE OPERATION**: This endpoint permanently deletes payment, transaction, and order data while preserving team/merchant information.

**Endpoint**: `POST /api/v1/Admin/clear-database`

**Use Cases**:
- Clean up test data in development/staging environments
- Reset system for new environment setup
- Clear accumulated test transactions

**Data Cleared**:
- **Payments**: All payment records and lifecycle data
- **Transactions**: All transaction records and processing details
- **Orders**: Order data embedded in payment records (OrderId field)
- **Related Audit Logs**: Audit trails for cleared payment/transaction data

**Data Preserved**:
- **Teams**: Merchant/team configurations and settings
- **Customers**: Customer profile information  
- **System Configuration**: Application settings and configurations

```bash
# Using Bearer token
curl -X POST "${BASE_URL}/api/v1/Admin/clear-database" \
  -H "Authorization: Bearer admin_token_2025_hackload_payment_gateway_secure_key_dev_only"

# Using custom header
curl -X POST "${BASE_URL}/api/v1/Admin/clear-database" \
  -H "X-Admin-Token: admin_token_2025_hackload_payment_gateway_secure_key_dev_only"
```

**Success Response**:
```json
{
  "success": true,
  "message": "Database cleared successfully",
  "statistics": {
    "deletedPayments": 150,
    "deletedTransactions": 300,
    "deletedOrders": 120,
    "operationDurationMs": 1250,
    "clearTimestamp": "2025-08-15T10:30:00Z"
  }
}
```

**Error Responses**:

**401 Unauthorized** (Missing or invalid token):
```json
{
  "error": "Missing Authorization",
  "message": "Admin token required. Use Authorization header with Bearer token or X-Admin-Token header."
}
```

**403 Forbidden** (Admin not configured):
```json
{
  "error": "Admin functionality not configured",
  "message": "Admin token must be configured in application settings to use admin endpoints"
}
```

**500 Internal Server Error** (Operation failed):
```json
{
  "error": "Clear Operation Failed", 
  "message": "Database transaction failed during clear operation"
}
```

### Admin Testing Script

```bash
#!/bin/bash

# Admin endpoint testing script
BASE_URL="http://localhost:5162"
ADMIN_TOKEN="admin_token_2025_hackload_payment_gateway_secure_key_dev_only"

echo "=== Admin Endpoint Testing ==="

# Test admin status
echo "1. Testing admin status..."
curl -s -X GET "${BASE_URL}/api/v1/Admin/status" | jq '.'

# Test clear database with Bearer token
echo -e "\n2. Testing clear database (Bearer token)..."
curl -s -X POST "${BASE_URL}/api/v1/Admin/clear-database" \
  -H "Authorization: Bearer ${ADMIN_TOKEN}" | jq '.'

# Test clear database with custom header  
echo -e "\n3. Testing clear database (X-Admin-Token header)..."
curl -s -X POST "${BASE_URL}/api/v1/Admin/clear-database" \
  -H "X-Admin-Token: ${ADMIN_TOKEN}" | jq '.'

echo -e "\nAdmin testing completed!"
```

## Core Payment API Endpoints

### 4. Initialize Payment

Creates a new payment transaction and returns payment URL.

**Endpoint**: `POST /api/v1/PaymentInit/init`

```bash
# Generate token first (example with demo-team)
TEAM_SLUG="demo-team"
PASSWORD_HASH="d3ad9315b7be5dd53b31a273b3b3aba5defe700808305aa16a3062b76658a791"

# For real implementation, you need to calculate the token
# This is a simplified example - implement proper token generation
curl -X POST "${BASE_URL}/api/v1/PaymentInit/init" \
  -H "Content-Type: application/json" \
  -d '{
    "teamSlug": "demo-team",
    "token": "CALCULATED_TOKEN_HERE",
    "amount": 150000,
    "orderId": "ORDER_12345",
    "currency": "RUB",
    "payType": "O",
    "description": "Test payment for order 12345",
    "customerKey": "CUSTOMER_001",
    "email": "customer@example.com",
    "phone": "+79123456789",
    "language": "ru",
    "successURL": "https://merchant.com/success",
    "failURL": "https://merchant.com/fail",
    "notificationURL": "https://merchant.com/webhook",
    "paymentExpiry": 30,
    "data": {
      "custom_field": "custom_value"
    },
    "items": [
      {
        "name": "Product 1",
        "quantity": 2,
        "price": 75000,
        "amount": 150000,
        "tax": "vat20",
        "category": "goods"
      }
    ]
  }'
```

**Response**:
```json
{
  "success": true,
  "errorCode": "0",
  "message": "Payment initialized successfully",
  "paymentId": "PAYMENT_123456",
  "status": "NEW",
  "paymentURL": "https://localhost:7162/api/v1/PaymentForm/render/PAYMENT_123456",
  "amount": 150000,
  "orderId": "ORDER_12345"
}
```

### 2. Check Payment Status

Retrieves current payment status and details.

**Endpoint**: `POST /api/v1/PaymentCheck/check`

```bash
curl -X POST "${BASE_URL}/api/v1/PaymentCheck/check" \
  -H "Content-Type: application/json" \
  -d '{
    "teamSlug": "demo-team",
    "token": "CALCULATED_TOKEN_HERE",
    "paymentId": "PAYMENT_123456"
  }'
```

**Alternative GET endpoint**:
```bash
# Check by Payment ID
curl -X GET "${BASE_URL}/api/v1/PaymentCheck/status?paymentId=PAYMENT_123456&teamSlug=demo-team&token=CALCULATED_TOKEN"

# Check by Order ID  
curl -X GET "${BASE_URL}/api/v1/PaymentCheck/status?orderId=ORDER_12345&teamSlug=demo-team&token=CALCULATED_TOKEN"
```

**Response**:
```json
{
  "success": true,
  "errorCode": "0",
  "message": "Payment status retrieved successfully",
  "paymentId": "PAYMENT_123456",
  "orderId": "ORDER_12345",
  "status": "CONFIRMED",
  "amount": 150000,
  "currency": "RUB"
}
```

### 3. Confirm Payment (Two-Stage)

Confirms (captures) a previously authorized payment for two-stage payments.

**Endpoint**: `POST /api/v1/PaymentConfirm/confirm`

```bash
curl -X POST "${BASE_URL}/api/v1/PaymentConfirm/confirm" \
  -H "Content-Type: application/json" \
  -d '{
    "teamSlug": "demo-team",
    "token": "CALCULATED_TOKEN_HERE",
    "paymentId": "PAYMENT_123456",
    "amount": 150000
  }'
```

### 4. Cancel Payment

Cancels a payment (refund for confirmed payments, void for authorized).

**Endpoint**: `POST /api/v1/PaymentCancel/cancel`

```bash
curl -X POST "${BASE_URL}/api/v1/PaymentCancel/cancel" \
  -H "Content-Type: application/json" \
  -d '{
    "teamSlug": "demo-team",
    "token": "CALCULATED_TOKEN_HERE",
    "paymentId": "PAYMENT_123456",
    "amount": 150000,
    "reason": "Customer request"
  }'
```

## Payment Form Endpoints

### 5. Get Payment Form

Renders the hosted payment form for customer card input.

**Endpoint**: `GET /api/v1/PaymentForm/render/{paymentId}`

```bash
# Open payment form in browser (no authentication required)
PAYMENT_ID="PAYMENT_123456"
curl -L "${BASE_URL}/api/v1/PaymentForm/render/${PAYMENT_ID}?lang=en" \
  -H "Accept: text/html"

# Or simply visit in browser:
# https://localhost:7162/api/v1/PaymentForm/render/PAYMENT_123456?lang=en
```

### 6. Submit Payment Form

Processes card data submission (typically done by the payment form).

**Endpoint**: `POST /api/v1/PaymentForm/submit`

```bash
curl -X POST "${BASE_URL}/api/v1/PaymentForm/submit" \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "paymentId=PAYMENT_123456&cardNumber=4111111111111111&cardExpiry=12/25&cvv=123&cardholderName=John+Doe"
```

## System Monitoring Endpoints

### 7. Health Check

**Endpoint**: `GET /health`

```bash
curl -X GET "${BASE_URL}/health" \
  -H "Accept: application/json"
```

**Response**:
```json
{
  "status": "Healthy",
  "totalDuration": "00:00:00.0234567",
  "entries": {
    "Database": {
      "data": {},
      "status": "Healthy",
      "duration": "00:00:00.0123456"
    }
  }
}
```

### 8. Metrics Endpoints

```bash
# Metrics Summary
curl -X GET "${BASE_URL}/api/metrics/summary"

# Payment Metrics
curl -X GET "${BASE_URL}/api/metrics/payments"

# System Metrics
curl -X GET "${BASE_URL}/api/metrics/system"

# Prometheus Metrics
curl -X GET "${BASE_URL}:8081/metrics"
```

## Token Generation Helper Script

Create a helper script to generate tokens for testing:

```bash
#!/bin/bash

# token_generator.sh
generate_token() {
    local team_slug="$1"
    local password_hash="$2"
    local request_json="$3"
    
    # Extract parameters from JSON and generate token
    python3 << EOF
import json
import hashlib
import sys

request_data = json.loads('$request_json')
team_slug = '$team_slug'
password_hash = '$password_hash'

# Generate token
token_params = {k: str(v) for k, v in request_data.items() if k.lower() != 'token'}
token_params['Password'] = password_hash
sorted_keys = sorted(token_params.keys())
concatenated = ''.join(token_params[key] for key in sorted_keys)
token = hashlib.sha256(concatenated.encode('utf-8')).hexdigest().lower()

# Add token to request and output
request_data['token'] = token
print(json.dumps(request_data, indent=2))
EOF
}

# Example usage
REQUEST='{"teamSlug":"demo-team","amount":150000,"orderId":"ORDER_12345","currency":"RUB"}'
generate_token "demo-team" "d3ad9315b7be5dd53b31a273b3b3aba5defe700808305aa16a3062b76658a791" "$REQUEST"
```

## Complete Testing Workflow

```bash
#!/bin/bash

# PaymentGateway API Testing Script
BASE_URL="https://localhost:7162"

echo "=== PaymentGateway API Testing Workflow ==="

# Note: In a real scenario, you would implement proper token generation
# For now, this script shows the structure but requires token calculation

TEAM_SLUG="demo-team"
ORDER_ID="TEST_ORDER_$(date +%s)"

echo "1. Testing with TeamSlug: $TEAM_SLUG"
echo "2. Order ID: $ORDER_ID"
echo ""
echo "To test this API, you need to:"
echo "1. Implement token generation logic (see examples above)"
echo "2. Calculate the SHA-256 token for each request"
echo "3. Include TeamSlug and Token in request body"
echo ""
echo "Available test teams:"
echo "- demo-team (password: demo123)"
echo "- test-team (password: test123)"
```

## Error Handling

All API endpoints return standardized error responses:

```json
{
  "success": false,
  "errorCode": "INVALID_REQUEST",
  "message": "Validation failed",
  "details": {
    "Amount": ["Amount is required"],
    "TeamSlug": ["TeamSlug is required for authentication"]
  }
}
```

### Common Error Codes

#### Team Registration Error Codes
- `2001` - Invalid request data or validation failed
- `2002` - Team slug already exists
- `2003` - Email already registered
- `2004` - Terms of service not accepted
- `2404` - Team not found
- `2429` - Registration rate limit exceeded

#### Payment Processing Error Codes
- `TEAM_SLUG_MISSING` - TeamSlug parameter is required
- `TOKEN_MISSING` - Token parameter is required  
- `INVALID_TOKEN` - Token validation failed
- `PAYMENT_NOT_FOUND` - Payment ID not found
- `INSUFFICIENT_FUNDS` - Not enough funds
- `PAYMENT_EXPIRED` - Payment session expired
- `INVALID_CARD` - Card validation failed

#### Administrative Error Codes
- `401` - Unauthorized (missing or invalid admin token)
- `403` - Forbidden (admin functionality not configured) 
- `500` - Internal server error during admin operations

#### General Error Codes
- `9999` - Internal server error

## Payment Status Values

| Status | Description |
|--------|-------------|
| NEW | Payment created, awaiting processing |
| FORM_SHOWED | Payment form displayed to customer |
| AUTHORIZING | Card authorization in progress |
| AUTHORIZED | Payment authorized (two-stage) |
| CONFIRMING | Payment confirmation in progress |
| CONFIRMED | Payment successfully processed |
| CANCELLED | Payment cancelled/refunded |
| REJECTED | Payment rejected by bank |

## Summary

To use this API you can either:

### Option 1: Use Existing Test Data
1. **Use seeded test data**: `demo-team` or `test-team` with their respective passwords
2. **Implement token generation**: SHA-256 hash of sorted parameters + password hash
3. **Include authentication**: `TeamSlug` and `Token` in every request body
4. **Handle responses**: Check `success` field and `errorCode` for status

### Option 2: Register Your Own Team
1. **Register a new team**: Use `POST /api/v1/TeamRegistration/register` endpoint
2. **Get your credentials**: Receive `teamSlug` and password hash from registration
3. **Implement token generation**: SHA-256 hash of sorted parameters + password hash
4. **Include authentication**: `TeamSlug` and `Token` in every request body
5. **Handle responses**: Check `success` field and `errorCode` for status

## Registration Workflow

```bash
# 1. Check if team slug is available
curl -X GET "${BASE_URL}/api/v1/TeamRegistration/check-availability/my-store" \
  -H "X-Admin-Token: admin_token_2025_hackload_payment_gateway_secure_key_dev_only"

# 2. Register your team
curl -X POST "${BASE_URL}/api/v1/TeamRegistration/register" \
  -H "Content-Type: application/json" \
  -H "X-Admin-Token: admin_token_2025_hackload_payment_gateway_secure_key_dev_only" \
  -d '{
    "teamSlug": "my-store",
    "password": "MySecurePass123!",
    "teamName": "My Store",
    "email": "admin@mystore.com",
    "successURL": "https://mystore.com/success",
    "failURL": "https://mystore.com/fail",
    "supportedCurrencies": "RUB,USD",
    "acceptTerms": true
  }'

# 3. Use your team credentials for payments
# (Generate token using teamSlug and password hash from registration response)
```

The key difference from typical APIs is the custom token-based authentication rather than standard Basic Auth or Bearer tokens.

## Admin Endpoints Quick Reference

| Endpoint | Method | Purpose | Authentication |
|----------|---------|---------|----------------|
| `/api/v1/Admin/status` | GET | Check admin service status | None required |
| `/api/v1/Admin/clear-database` | POST | Clear payment/transaction data | Bearer token or X-Admin-Token header |

**Admin Token Configuration**:
```json
{
  "AdminAuthentication": {
    "AdminToken": "your_secure_admin_token_here",
    "TokenHeaderName": "X-Admin-Token"
  }
}
```

**Security Notes**:
- Admin tokens are SHA-256 hashed for validation
- Clear database operation is **irreversible**
- All admin operations are thoroughly logged
- Only use admin endpoints in development/staging environments
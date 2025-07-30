# PaymentGateway.API Automated Test Scenarios

## Overview

This document defines structured test cases for automated testing of the PaymentGateway.API service. Each test case includes scenario description, input data, expected output results, and validation criteria for test automation frameworks.

## Test Configuration

```json
{
  "baseUrl": "http://localhost:5162",
  "testTeam": {
    "teamSlug": "test-merchant",
    "validToken": "eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9.valid.token",
    "invalidToken": "invalid-token-123"
  },
  "testCards": {
    "validVisa": "4111111111111111",
    "validMastercard": "5555555555554444",
    "invalidCard": "4000000000000002",
    "expiredCard": "4111111111111111"
  },
  "testData": {
    "validEmail": "test@example.com",
    "invalidEmail": "invalid-email",
    "validPhone": "+7-900-123-4567",
    "invalidPhone": "123"
  }
}
```

## Test Suite 1: Payment Initialization

### TC001 - Valid Payment Initialization

**Scenario:** Initialize payment with valid merchant credentials and payment data

**HTTP Request:**
```http
POST /api/v1/paymentinit/init
Content-Type: application/json

{
  "teamSlug": "test-merchant",
  "token": "eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9.valid.token",
  "amount": 150000,
  "orderId": "ORDER-TC001-001",
  "currency": "RUB",
  "description": "Test payment for automated testing",
  "successURL": "https://example.com/success",
  "failURL": "https://example.com/fail",
  "notificationURL": "https://example.com/webhook",
  "paymentExpiry": 30,
  "email": "test@example.com",
  "language": "en"
}
```

**Expected Response:**
```json
{
  "success": true,
  "paymentId": "{string:regex:^pay_[a-zA-Z0-9]+$}",
  "orderId": "ORDER-TC001-001",
  "status": "NEW",
  "amount": 150000,
  "currency": "RUB",
  "paymentURL": "{string:regex:^http://localhost:5162/api/v1/paymentform/render/pay_[a-zA-Z0-9]+$}",
  "expiresAt": "{datetime:future}",
  "createdAt": "{datetime:now}",
  "errorCode": null,
  "message": null
}
```

**Validation Rules:**
- HTTP Status Code: 200
- Response time: < 2000ms
- `paymentId` must match pattern `pay_[alphanumeric]+`
- `status` must be "NEW"
- `expiresAt` must be 30 minutes in the future
- `paymentURL` must be valid and accessible

### TC002 - Invalid Team Slug

**Scenario:** Attempt to initialize payment with non-existent team slug

**HTTP Request:**
```http
POST /api/v1/paymentinit/init
Content-Type: application/json

{
  "teamSlug": "non-existent-merchant",
  "token": "eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9.valid.token",
  "amount": 150000,
  "orderId": "ORDER-TC002-001",
  "currency": "RUB"
}
```

**Expected Response:**
```json
{
  "success": false,
  "paymentId": "",
  "orderId": "",
  "status": "ERROR",
  "amount": 0,
  "currency": "",
  "paymentURL": null,
  "expiresAt": null,
  "createdAt": null,
  "errorCode": "1001",
  "message": "Authentication failed"
}
```

**Validation Rules:**
- HTTP Status Code: 401
- Response time: < 1000ms
- `success` must be false
- `errorCode` must be "1001"

### TC003 - Invalid Token

**Scenario:** Initialize payment with invalid authentication token

**HTTP Request:**
```http
POST /api/v1/paymentinit/init
Content-Type: application/json

{
  "teamSlug": "test-merchant",
  "token": "invalid-token-123",
  "amount": 150000,
  "orderId": "ORDER-TC003-001",
  "currency": "RUB"
}
```

**Expected Response:**
```json
{
  "success": false,
  "paymentId": "",
  "orderId": "",
  "status": "ERROR",
  "amount": 0,
  "currency": "",
  "paymentURL": null,
  "expiresAt": null,
  "createdAt": null,
  "errorCode": "1001",
  "message": "Authentication failed"
}
```

**Validation Rules:**
- HTTP Status Code: 401
- `errorCode` must be "1001"

### TC004 - Invalid Amount (Negative)

**Scenario:** Initialize payment with negative amount

**HTTP Request:**
```http
POST /api/v1/paymentinit/init
Content-Type: application/json

{
  "teamSlug": "test-merchant",
  "token": "eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9.valid.token",
  "amount": -1000,
  "orderId": "ORDER-TC004-001",
  "currency": "RUB"
}
```

**Expected Response:**
```json
{
  "success": false,
  "paymentId": "",
  "orderId": "",
  "status": "ERROR",
  "amount": 0,
  "currency": "",
  "paymentURL": null,
  "expiresAt": null,
  "createdAt": null,
  "errorCode": "1100",
  "message": "Validation failed",
  "details": {
    "description": "{string:contains:Amount must be greater than zero}"
  }
}
```

**Validation Rules:**
- HTTP Status Code: 400
- `errorCode` must be "1100"
- Error message must contain amount validation

### TC005 - Invalid Amount (Excessive)

**Scenario:** Initialize payment with amount exceeding maximum limit

**HTTP Request:**
```http
POST /api/v1/paymentinit/init
Content-Type: application/json

{
  "teamSlug": "test-merchant",
  "token": "eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9.valid.token",
  "amount": 100000001,
  "orderId": "ORDER-TC005-001",
  "currency": "RUB"
}
```

**Expected Response:**
```json
{
  "success": false,
  "paymentId": "",
  "orderId": "",
  "status": "ERROR",
  "amount": 0,
  "currency": "",
  "paymentURL": null,
  "expiresAt": null,
  "createdAt": null,
  "errorCode": "1100",
  "message": "Validation failed",
  "details": {
    "description": "{string:contains:Amount exceeds maximum limit}"
  }
}
```

**Validation Rules:**
- HTTP Status Code: 400
- `errorCode` must be "1100"

### TC006 - Invalid Currency

**Scenario:** Initialize payment with unsupported currency

**HTTP Request:**
```http
POST /api/v1/paymentinit/init
Content-Type: application/json

{
  "teamSlug": "test-merchant",
  "token": "eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9.valid.token",
  "amount": 150000,
  "orderId": "ORDER-TC006-001",
  "currency": "JPY"
}
```

**Expected Response:**
```json
{
  "success": false,
  "paymentId": "",
  "orderId": "",
  "status": "ERROR",
  "amount": 0,
  "currency": "",
  "paymentURL": null,
  "expiresAt": null,
  "createdAt": null,
  "errorCode": "1100",
  "message": "Validation failed",
  "details": {
    "description": "{string:contains:Currency must be one of: RUB, USD, EUR}"
  }
}
```

**Validation Rules:**
- HTTP Status Code: 400
- `errorCode` must be "1100"

## Test Suite 2: Payment Form Rendering

### TC007 - Valid Payment Form Rendering

**Scenario:** Render payment form for valid payment ID

**Prerequisites:** Valid payment created with TC001

**HTTP Request:**
```http
GET /api/v1/paymentform/render/{paymentId}?lang=en
Accept: text/html
```

**Expected Response:**
- HTTP Status Code: 200
- Content-Type: text/html; charset=utf-8
- Response time: < 1000ms

**HTML Content Validation:**
- Contains payment form with payment ID
- Contains CSRF token
- Contains card input fields (cardNumber, expiryDate, cvv, cardholderName)
- Contains email input field
- Contains amount and currency display
- Form action points to submit endpoint

### TC008 - Invalid Payment ID Format

**Scenario:** Attempt to render form with malformed payment ID

**HTTP Request:**
```http
GET /api/v1/paymentform/render/invalid-payment-id
Accept: text/html
```

**Expected Response:**
```json
{
  "error": "Invalid payment ID format"
}
```

**Validation Rules:**
- HTTP Status Code: 400
- Response time: < 500ms

### TC009 - Non-existent Payment ID

**Scenario:** Attempt to render form for non-existent payment

**HTTP Request:**
```http
GET /api/v1/paymentform/render/pay_nonexistent123
Accept: text/html
```

**Expected Response:**
```json
{
  "error": "Payment not found"
}
```

**Validation Rules:**
- HTTP Status Code: 404
- Response time: < 500ms

## Test Suite 3: Payment Form Submission

### TC010 - Valid Form Submission (Successful Payment)

**Scenario:** Submit payment form with valid card data

**Prerequisites:** Valid payment form rendered from TC007

**HTTP Request:**
```http
POST /api/v1/paymentform/submit
Content-Type: application/x-www-form-urlencoded

paymentId={validPaymentId}&cardNumber=4111111111111111&expiryDate=12/25&cvv=123&cardholderName=John Doe&email=test@example.com&csrfToken={validCsrfToken}
```

**Expected Response:**
- HTTP Status Code: 200
- Content-Type: text/html; charset=utf-8
- Response time: < 3000ms

**HTML Content Validation:**
- Contains success message
- Contains payment ID
- Contains "Payment authorized successfully" text
- If successURL provided, contains redirect button

### TC011 - Invalid Card Number (Luhn Check Failure)

**Scenario:** Submit form with invalid card number that fails Luhn algorithm

**HTTP Request:**
```http
POST /api/v1/paymentform/submit
Content-Type: application/x-www-form-urlencoded

paymentId={validPaymentId}&cardNumber=1234567890123456&expiryDate=12/25&cvv=123&cardholderName=John Doe&email=test@example.com&csrfToken={validCsrfToken}
```

**Expected Response:**
```json
{
  "error": "Validation failed",
  "details": [
    "Invalid card number"
  ],
  "paymentId": "{validPaymentId}"
}
```

**Validation Rules:**
- HTTP Status Code: 400
- Response time: < 1000ms

### TC012 - Expired Card

**Scenario:** Submit form with expired card

**HTTP Request:**
```http
POST /api/v1/paymentform/submit
Content-Type: application/x-www-form-urlencoded

paymentId={validPaymentId}&cardNumber=4111111111111111&expiryDate=01/20&cvv=123&cardholderName=John Doe&email=test@example.com&csrfToken={validCsrfToken}
```

**Expected Response:**
```json
{
  "error": "Validation failed",
  "details": [
    "Invalid or expired card"
  ],
  "paymentId": "{validPaymentId}"
}
```

**Validation Rules:**
- HTTP Status Code: 400
- Error details must contain expiry validation

### TC013 - Invalid CVV

**Scenario:** Submit form with invalid CVV format

**HTTP Request:**
```http
POST /api/v1/paymentform/submit
Content-Type: application/x-www-form-urlencoded

paymentId={validPaymentId}&cardNumber=4111111111111111&expiryDate=12/25&cvv=12&cardholderName=John Doe&email=test@example.com&csrfToken={validCsrfToken}
```

**Expected Response:**
```json
{
  "error": "Validation failed",
  "details": [
    "Invalid CVV"
  ],
  "paymentId": "{validPaymentId}"
}
```

**Validation Rules:**
- HTTP Status Code: 400

### TC014 - Missing CSRF Token

**Scenario:** Submit form without CSRF token

**HTTP Request:**
```http
POST /api/v1/paymentform/submit
Content-Type: application/x-www-form-urlencoded

paymentId={validPaymentId}&cardNumber=4111111111111111&expiryDate=12/25&cvv=123&cardholderName=John Doe&email=test@example.com
```

**Expected Response:**
```json
{
  "error": "Invalid security token"
}
```

**Validation Rules:**
- HTTP Status Code: 400
- Response time: < 500ms

### TC015 - Invalid CSRF Token

**Scenario:** Submit form with invalid CSRF token

**HTTP Request:**
```http
POST /api/v1/paymentform/submit
Content-Type: application/x-www-form-urlencoded

paymentId={validPaymentId}&cardNumber=4111111111111111&expiryDate=12/25&cvv=123&cardholderName=John Doe&email=test@example.com&csrfToken=invalid-csrf-token
```

**Expected Response:**
```json
{
  "error": "Invalid security token"
}
```

**Validation Rules:**
- HTTP Status Code: 400

## Test Suite 4: Payment Status Management

### TC016 - Check Payment Status (NEW)

**Scenario:** Check status of newly created payment

**Prerequisites:** Valid payment created with TC001

**HTTP Request:**
```http
GET /api/v1/paymentcheck/status/{paymentId}
Authorization: Bearer {validToken}
```

**Expected Response:**
```json
{
  "paymentId": "{validPaymentId}",
  "status": "NEW",
  "amount": 150000,
  "currency": "RUB",
  "orderId": "ORDER-TC001-001",
  "createdAt": "{datetime}",
  "expiresAt": "{datetime:future}",
  "updatedAt": "{datetime}"
}
```

**Validation Rules:**
- HTTP Status Code: 200
- Response time: < 1000ms
- `status` must be "NEW"

### TC017 - Check Payment Status (AUTHORIZED)

**Scenario:** Check status after successful form submission

**Prerequisites:** Successful form submission from TC010

**HTTP Request:**
```http
GET /api/v1/paymentcheck/status/{paymentId}
Authorization: Bearer {validToken}
```

**Expected Response:**
```json
{
  "paymentId": "{validPaymentId}",
  "status": "AUTHORIZED",
  "amount": 150000,
  "currency": "RUB",
  "orderId": "ORDER-TC001-001",
  "createdAt": "{datetime}",
  "expiresAt": "{datetime:future}",
  "updatedAt": "{datetime}",
  "cardMask": "411111******1111"
}
```

**Validation Rules:**
- HTTP Status Code: 200
- `status` must be "AUTHORIZED"
- `cardMask` must be present and properly masked

### TC018 - Payment Confirmation

**Scenario:** Confirm authorized payment

**Prerequisites:** Payment in AUTHORIZED status from TC017

**HTTP Request:**
```http
POST /api/v1/paymentconfirm/confirm/{paymentId}
Content-Type: application/json
Authorization: Bearer {validToken}

{
  "confirmationData": {
    "confirmedBy": "automated-test",
    "confirmationTime": "2025-01-30T12:00:00Z"
  }
}
```

**Expected Response:**
```json
{
  "success": true,
  "paymentId": "{validPaymentId}",
  "status": "CONFIRMED",
  "message": "Payment confirmed successfully",
  "confirmedAt": "{datetime:now}"
}
```

**Validation Rules:**
- HTTP Status Code: 200
- Response time: < 2000ms
- `success` must be true
- `status` must be "CONFIRMED"

### TC019 - Payment Cancellation

**Scenario:** Cancel payment in NEW status

**Prerequisites:** Valid payment in NEW status

**HTTP Request:**
```http
POST /api/v1/paymentcancel/cancel/{paymentId}
Content-Type: application/json
Authorization: Bearer {validToken}

{
  "reason": "Customer requested cancellation",
  "cancelledBy": "automated-test"
}
```

**Expected Response:**
```json
{
  "success": true,
  "paymentId": "{validPaymentId}",
  "status": "CANCELLED",
  "message": "Payment cancelled successfully",
  "cancelledAt": "{datetime:now}",
  "cancellationReason": "Customer requested cancellation"
}
```

**Validation Rules:**
- HTTP Status Code: 200
- `success` must be true
- `status` must be "CANCELLED"

### TC020 - Invalid State Transition (Double Confirmation)

**Scenario:** Attempt to confirm already confirmed payment

**Prerequisites:** Payment in CONFIRMED status from TC018

**HTTP Request:**
```http
POST /api/v1/paymentconfirm/confirm/{paymentId}
Content-Type: application/json
Authorization: Bearer {validToken}

{
  "confirmationData": {
    "confirmedBy": "automated-test-2",
    "confirmationTime": "2025-01-30T12:30:00Z"
  }
}
```

**Expected Response:**
```json
{
  "success": false,
  "paymentId": "{validPaymentId}",
  "status": "CONFIRMED",
  "message": "Payment is already in CONFIRMED status and cannot be confirmed again",
  "errorCode": "INVALID_STATE_TRANSITION"
}
```

**Validation Rules:**
- HTTP Status Code: 400
- `success` must be false
- Error message must indicate invalid state transition

## Test Suite 5: Security and Rate Limiting

### TC021 - Rate Limiting (Payment Initialization)

**Scenario:** Exceed rate limit for payment initialization

**HTTP Request:** Send 101 rapid requests
```http
POST /api/v1/paymentinit/init
Content-Type: application/json

{
  "teamSlug": "test-merchant",
  "token": "eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9.valid.token",
  "amount": 150000,
  "orderId": "ORDER-RATE-{requestNumber}",
  "currency": "RUB"
}
```

**Expected Response (after rate limit exceeded):**
```json
{
  "success": false,
  "paymentId": "",
  "orderId": "",
  "status": "ERROR",
  "amount": 0,
  "currency": "",
  "paymentURL": null,
  "expiresAt": null,
  "createdAt": null,
  "errorCode": "1429",
  "message": "Rate limit exceeded"
}
```

**Validation Rules:**
- HTTP Status Code: 429
- Rate limit should trigger after ~100 requests
- `errorCode` must be "1429"

### TC022 - SQL Injection Protection

**Scenario:** Attempt SQL injection in payment description

**HTTP Request:**
```http
POST /api/v1/paymentinit/init
Content-Type: application/json

{
  "teamSlug": "test-merchant",
  "token": "eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9.valid.token",
  "amount": 150000,
  "orderId": "ORDER-SQL-001",
  "currency": "RUB",
  "description": "'; DROP TABLE payments; --"
}
```

**Expected Response:**
```json
{
  "success": true,
  "paymentId": "{string:regex:^pay_[a-zA-Z0-9]+$}",
  "orderId": "ORDER-SQL-001",
  "status": "NEW",
  "amount": 150000,
  "currency": "RUB",
  "paymentURL": "{validPaymentURL}",
  "expiresAt": "{datetime:future}",
  "createdAt": "{datetime:now}"
}
```

**Validation Rules:**
- HTTP Status Code: 200
- Payment should be created successfully (SQL injection prevented)
- Database integrity maintained (verify no tables dropped)

### TC023 - XSS Protection

**Scenario:** Attempt XSS injection in payment description

**HTTP Request:**
```http
POST /api/v1/paymentinit/init
Content-Type: application/json

{
  "teamSlug": "test-merchant",
  "token": "eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9.valid.token",
  "amount": 150000,
  "orderId": "ORDER-XSS-001",
  "currency": "RUB",
  "description": "<script>alert('xss')</script>"
}
```

**Expected Response:**
- HTTP Status Code: 200
- Payment created successfully

**Additional Validation:**
- When payment form is rendered, script tags must be HTML-encoded
- HTML output must not contain executable JavaScript
- Description in HTML must be: `&lt;script&gt;alert('xss')&lt;/script&gt;`

## Test Suite 6: Edge Cases and Error Handling

### TC024 - Malformed JSON Request

**Scenario:** Send malformed JSON in request body

**HTTP Request:**
```http
POST /api/v1/paymentinit/init
Content-Type: application/json

{
  "teamSlug": "test-merchant",
  "token": "valid-token",
  "amount": invalid-json-value,
  "currency": "RUB"
}
```

**Expected Response:**
```json
{
  "success": false,
  "paymentId": "",
  "orderId": "",
  "status": "ERROR",
  "amount": 0,
  "currency": "",
  "paymentURL": null,
  "expiresAt": null,
  "createdAt": null,
  "errorCode": "1000",
  "message": "Invalid request"
}
```

**Validation Rules:**
- HTTP Status Code: 400
- Response time: < 500ms

### TC025 - Empty Request Body

**Scenario:** Send empty request body

**HTTP Request:**
```http
POST /api/v1/paymentinit/init
Content-Type: application/json

```

**Expected Response:**
```json
{
  "success": false,
  "paymentId": "",
  "orderId": "",
  "status": "ERROR",
  "amount": 0,
  "currency": "",
  "paymentURL": null,
  "expiresAt": null,
  "createdAt": null,
  "errorCode": "1000",
  "message": "Invalid request"
}
```

**Validation Rules:**
- HTTP Status Code: 400

### TC026 - Service Timeout Simulation

**Scenario:** Test service behavior under timeout conditions

**HTTP Request:**
```http
POST /api/v1/paymentinit/init
Content-Type: application/json
Request-Timeout: 1ms

{
  "teamSlug": "test-merchant",
  "token": "eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9.valid.token",
  "amount": 150000,
  "orderId": "ORDER-TIMEOUT-001",
  "currency": "RUB"
}
```

**Expected Response:**
```json
{
  "success": false,
  "paymentId": "",
  "orderId": "",
  "status": "ERROR",
  "amount": 0,
  "currency": "",
  "paymentURL": null,
  "expiresAt": null,
  "createdAt": null,
  "errorCode": "1408",
  "message": "Request timeout"
}
```

**Validation Rules:**
- HTTP Status Code: 408
- Response within reasonable timeout period

## Test Suite 7: Metrics and Monitoring

### TC027 - Metrics Endpoint Access

**Scenario:** Access Prometheus metrics endpoint

**HTTP Request:**
```http
GET /api/v1/metrics
Accept: text/plain
```

**Expected Response:**
- HTTP Status Code: 200
- Content-Type: text/plain; version=0.0.4; charset=utf-8
- Response time: < 1000ms

**Content Validation:**
- Contains payment initialization metrics
- Contains payment form metrics
- Contains authentication metrics
- Proper Prometheus format

### TC028 - Team-specific Metrics

**Scenario:** Get payment metrics for specific team

**HTTP Request:**
```http
GET /api/v1/paymentinit/metrics/test-merchant
Authorization: Bearer {validToken}
```

**Expected Response:**
```json
{
  "teamSlug": "test-merchant",
  "totalPayments": "{number:>=0}",
  "successfulPayments": "{number:>=0}",
  "failedPayments": "{number:>=0}",
  "totalAmount": "{number:>=0}",
  "averageAmount": "{number:>=0}",
  "currencies": [
    {
      "currency": "RUB",
      "count": "{number:>=0}",
      "totalAmount": "{number:>=0}"
    }
  ],
  "timeRange": {
    "from": "{datetime}",
    "to": "{datetime}"
  }
}
```

**Validation Rules:**
- HTTP Status Code: 200
- Response time: < 2000ms
- All numeric values must be non-negative

## Test Execution Framework

### Data-Driven Test Configuration

```json
{
  "testSuites": [
    {
      "name": "Payment Initialization",
      "testCases": ["TC001", "TC002", "TC003", "TC004", "TC005", "TC006"],
      "parallel": true,
      "timeout": 10000
    },
    {
      "name": "Payment Form",
      "testCases": ["TC007", "TC008", "TC009"],
      "parallel": false,
      "timeout": 5000,
      "dependencies": ["TC001"]
    },
    {
      "name": "Form Submission",
      "testCases": ["TC010", "TC011", "TC012", "TC013", "TC014", "TC015"],
      "parallel": false,
      "timeout": 15000,
      "dependencies": ["TC007"]
    },
    {
      "name": "Status Management",
      "testCases": ["TC016", "TC017", "TC018", "TC019", "TC020"],
      "parallel": false,
      "timeout": 10000,
      "dependencies": ["TC010"]
    },
    {
      "name": "Security",
      "testCases": ["TC021", "TC022", "TC023"],
      "parallel": true,
      "timeout": 30000
    },
    {
      "name": "Error Handling",
      "testCases": ["TC024", "TC025", "TC026"],
      "parallel": true,
      "timeout": 5000
    },
    {
      "name": "Monitoring",
      "testCases": ["TC027", "TC028"],
      "parallel": true,
      "timeout": 5000
    }
  ]
}
```

### Validation Functions

```javascript
// Example validation functions for automated test frameworks

function validatePaymentId(paymentId) {
  return /^pay_[a-zA-Z0-9]+$/.test(paymentId);
}

function validateDateTime(dateTime, type) {
  const date = new Date(dateTime);
  const now = new Date();
  
  switch(type) {
    case 'now':
      return Math.abs(date - now) < 60000; // Within 1 minute
    case 'future':
      return date > now;
    default:
      return !isNaN(date.getTime());
  }
}

function validateHttpStatus(actual, expected) {
  return actual === expected;
}

function validateResponseTime(duration, maxMs) {
  return duration < maxMs;
}

function validateStringContains(str, substring) {
  return str && str.includes(substring);
}

function validateRegex(str, pattern) {
  return new RegExp(pattern).test(str);
}
```

## Test Environment Requirements

### Prerequisites
- PaymentGateway.API service running on localhost:5162
- Test database with sample merchant data
- Valid test merchant credentials configured
- Rate limiting reset capability for TC021

### Test Data Cleanup
- Each test should clean up created payments after execution
- Shared test resources should be isolated
- Database state should be reset between test suite runs

### Performance Baselines
- Payment initialization: < 2000ms
- Form rendering: < 1000ms
- Form submission: < 3000ms
- Status checks: < 1000ms
- Metrics collection: < 2000ms

This structured test specification enables automated testing frameworks to execute comprehensive validation of the PaymentGateway.API service functionality, security, and performance characteristics.
# PaymentGateway External API Tests

Comprehensive external testing suite for the PaymentGateway.sln project, covering the complete payment lifecycle based on the specifications in `specs/payment-lifecycle.md`.

## Overview

This test suite validates the PaymentGateway API implementation using external HTTP requests. It tests the actual server implementation with proper SHA-256 HMAC token authentication as specified in the API reference documentation.

## Test Coverage

### 1. Team Registration API Tests
- ✅ Valid team registration with admin token
- ✅ Team slug availability checking
- ✅ Authentication failures (missing admin token)
- ✅ Team status retrieval

### 2. Payment Initialization Tests
- ✅ Valid payment initialization with proper token
- ✅ Authentication failures (missing/invalid tokens)
- ✅ Invalid team slug handling
- ✅ Amount validation (negative amounts, excessive amounts)
- ✅ Currency validation (unsupported currencies)
- ✅ URL format validation

### 3. Payment Lifecycle Tests
Based on `specs/payment-lifecycle.md` state transitions:
- ✅ Payment initialization (INIT → NEW)
- ✅ Payment form rendering (NEW → FORM_SHOWED)
- ✅ Payment status checking
- ✅ Payment confirmation (AUTHORIZED → CONFIRMED)
- ✅ Payment cancellation (NEW/CONFIRMED → CANCELLED)
- ✅ Non-existent payment ID handling

### 4. Security and Error Handling Tests
- ✅ Malformed JSON handling
- ✅ SQL injection attempt protection
- ✅ XSS attempt sanitization
- ✅ Missing required fields validation
- ✅ Invalid URL format validation

### 5. System Endpoints Tests
- ✅ Health check endpoint
- ✅ Metrics endpoints (Prometheus format)
- ✅ API metrics summary
- ✅ Payment-specific metrics
- ✅ System metrics

### 6. Token Generation Tests
- ✅ SHA-256 token generation validation
- ✅ Parameter order independence verification

## Prerequisites

1. **Server Running**: The PaymentGateway server must be running
   ```bash
   dotnet run --project PaymentGateway.API
   ```

2. **Python 3**: Required for SHA-256 token generation
   ```bash
   python3 --version  # Should be 3.6+
   ```

3. **Dependencies**: Standard system tools (curl, bash)

## Usage

### Basic Test Execution

```bash
# Run all tests with default settings
./PaymentGateway.Tests/External/payment-gateway-tests.sh
```

### Custom Configuration

```bash
# Run with custom base URL
BASE_URL="https://localhost:7162" ./PaymentGateway.Tests/External/payment-gateway-tests.sh

# Run with HTTP instead of HTTPS
BASE_URL="http://localhost:5162" ./PaymentGateway.Tests/External/payment-gateway-tests.sh
```

## Authentication Implementation

The tests implement the exact SHA-256 HMAC token authentication specified in `docs/api-reference.md`:

1. **Collect parameters** from request (excluding Token itself)
2. **Add password hash** for the team
3. **Sort parameters alphabetically** by key
4. **Concatenate values** in sorted order
5. **Generate SHA-256 hash** of concatenated string

### Test Team Credentials

The tests use the predefined test teams from the API reference:

| Team Slug | Password | Password Hash |
|-----------|----------|---------------|
| `demo-team` | `demo123` | `d3ad9315b7be5dd53b31a273b3b3aba5defe700808305aa16a3062b76658a791` |
| `test-team` | `test123` | `ecd71870d1963316a97e3ac3408c9835ad8cf0f3c1bc703527c30265534f75ae` |

## Test Output

The script provides detailed output with color-coded results:

```
===============================
PaymentGateway External API Tests
===============================
Base URL: http://localhost:5162
Timestamp: 2025-01-30 12:00:00

[12:00:01] Checking server health...
✅ Server is healthy
✅ Test environment ready

=== Team Registration API Tests ===
[12:00:02] Running test: Team Registration - Valid Request
✅ Team Registration - Valid Request (HTTP 200)
---
[12:00:03] Running test: Check Team Availability - Available Slug
✅ Check Team Availability - Available Slug (HTTP 200)
---

... (additional test output) ...

===============================
        TEST SUMMARY
===============================
Total Tests:  45
Passed:       43
Failed:       2
Status:       TESTS FAILED
===============================
```

## Integration with CI/CD

The script returns appropriate exit codes:
- `0`: All tests passed
- `1`: One or more tests failed

Example CI integration:

```yaml
- name: Run PaymentGateway External Tests
  run: |
    # Start server in background
    dotnet run --project PaymentGateway.API &
    SERVER_PID=$!
    
    # Wait for server to start
    sleep 10
    
    # Run tests
    ./PaymentGateway.Tests/External/payment-gateway-tests.sh
    
    # Clean up
    kill $SERVER_PID
```

## Architecture Validation

The tests validate the actual server implementation (`PaymentGateway.API`) against:

1. **API Reference Documentation** (`docs/api-reference.md`)
2. **Payment Lifecycle Specification** (`specs/payment-lifecycle.md`)
3. **Server Source Code** (ground truth for authentication logic)

Key validation points:
- SHA-256 HMAC token authentication matches server implementation
- Payment state transitions follow the lifecycle specification
- Error handling and status codes match API documentation
- Security measures (injection protection, input validation)

## Troubleshooting

### Server Not Running
```
❌ Server health check failed: CONNECTION_ERROR
Please start the PaymentGateway server first:
  dotnet run --project PaymentGateway.API
```

### Python Dependencies Missing
```
❌ Python3 is required for token generation but not installed
❌ Required Python modules (hashlib, json) are not available
```

### Authentication Failures
If authentication consistently fails, verify:
1. Team credentials match the server database
2. Token generation algorithm matches server implementation
3. Server is using the expected authentication middleware

## Contributing

When adding new tests:

1. Follow the existing test structure (`test_*` functions)
2. Use the `run_test` helper function for consistent output
3. Include both positive and negative test cases
4. Update the test coverage documentation above
5. Ensure tests are idempotent and don't interfere with each other

## Related Files

- `PaymentGateway.API/Controllers/PaymentInitController.cs` - Payment initialization logic
- `PaymentGateway.API/Middleware/PaymentAuthenticationMiddleware.cs` - Authentication middleware
- `docs/api-reference.md` - Complete API documentation
- `specs/payment-lifecycle.md` - Payment state transition specification
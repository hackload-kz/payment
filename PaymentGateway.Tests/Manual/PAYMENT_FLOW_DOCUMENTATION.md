# PaymentGateway Payment Flow Documentation

## Overview

This document describes the complete payment lifecycle in the PaymentGateway system, including API endpoints, authentication, database operations, and testing procedures.

## Architecture Components

### Core Controllers
- **TeamRegistrationController** - Team management and registration
- **PaymentInitController** - Payment initialization
- **PaymentCheckController** - Payment status checking
- **PaymentConfirmController** - Payment confirmation
- **PaymentCancelController** - Payment cancellation

### Database Schema
- **payment.teams** - Merchant/team information
- **payment.payments** - Payment records
- **payment.transactions** - Transaction details
- **payment.audit_logs** - Audit trail

## Payment Lifecycle Flow

### 1. Team Registration Phase

**Endpoint:** `POST /api/v1/TeamRegistration/register`

**Authentication:** Requires admin token (`X-Admin-Token` header)

**Request Example:**
```json
{
    "teamSlug": "my-store-2025",
    "teamName": "My Store",
    "password": "SecurePassword123!",
    "email": "admin@mystore.com",
    "phone": "+1234567890",
    "successURL": "https://mystore.com/success",
    "failURL": "https://mystore.com/fail",
    "notificationURL": "https://mystore.com/webhook",
    "supportedCurrencies": "RUB,USD,EUR",
    "businessInfo": {
        "businessType": "ecommerce",
        "description": "Online retail store"
    },
    "acceptTerms": true
}
```

**Response:** Returns team details including `teamId`, `passwordHashPreview`, and API endpoint.

**Database Impact:** Creates record in `payment.teams` table.

### 2. Payment Initialization Phase

**Endpoint:** `POST /api/v1/PaymentInit`

**Authentication:** Requires HMAC-SHA256 token based on team credentials

**Request Example:**
```json
{
    "teamSlug": "my-store-2025",
    "token": "calculated_hmac_sha256_token",
    "amount": 250000,
    "orderId": "order-12345",
    "currency": "RUB",
    "payType": "O",
    "description": "Purchase of goods",
    "customerKey": "customer-123",
    "email": "customer@example.com",
    "phone": "+79991234567",
    "language": "ru",
    "successURL": "https://merchant.com/success",
    "failURL": "https://merchant.com/fail",
    "notificationURL": "https://merchant.com/webhook",
    "paymentExpiry": 30,
    "items": [
        {
            "name": "Product Name",
            "quantity": 1,
            "price": 250000,
            "amount": 250000,
            "tax": "vat20"
        }
    ]
}
```

**Response:** Returns `paymentId`, `paymentURL`, and session details.

**Database Impact:** Creates records in `payment.payments` and potentially `payment.transactions`.

### 3. Payment Status Checking

**Endpoints:**
- `POST /api/v1/PaymentCheck` - With authentication
- `GET /api/v1/PaymentCheck?paymentId={id}&teamSlug={slug}` - Alternative method

**Authentication:** Requires team token for POST, optional for GET

**Response:** Returns current payment status (PENDING, CONFIRMED, CANCELLED, FAILED, etc.)

### 4. Payment Confirmation

**Endpoint:** `POST /api/v1/PaymentConfirm`

**Authentication:** Requires team token

**Use Case:** Confirms a two-stage payment or manual confirmation

**Database Impact:** Updates payment status to CONFIRMED

### 5. Payment Cancellation

**Endpoint:** `POST /api/v1/PaymentCancel`

**Authentication:** Requires team token

**Use Case:** Cancels pending or authorized payments

**Database Impact:** Updates payment status to CANCELLED

## Authentication System

### Team Token Generation

The system uses HMAC-SHA256 for request authentication:

1. **Key**: Team's secret key (stored securely in database)
2. **Data**: Concatenated request parameters in specific order
3. **Algorithm**: SHA256 hash of concatenated string
4. **Format**: Lowercase hexadecimal string

### Token Calculation Steps
1. Sort request parameters alphabetically by key
2. Concatenate values with team's secret key
3. Calculate SHA256 hash
4. Convert to lowercase hex string

## Database Operations

### Teams Table Structure
```sql
CREATE TABLE payment.teams (
    id UUID PRIMARY KEY,
    team_slug VARCHAR(50) UNIQUE NOT NULL,
    team_name VARCHAR(255) NOT NULL,
    password_hash VARCHAR(255) NOT NULL,
    contact_email VARCHAR(254),
    contact_phone VARCHAR(20),
    supported_currencies TEXT[],
    success_url TEXT,
    fail_url TEXT,
    notification_url TEXT,
    is_active BOOLEAN DEFAULT true,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    failed_authentication_attempts INTEGER DEFAULT 0,
    -- Additional audit fields
    row_version BYTEA,
    created_by VARCHAR(100),
    updated_by VARCHAR(100)
);
```

### Payments Table (Expected Structure)
```sql
CREATE TABLE payment.payments (
    payment_id UUID PRIMARY KEY,
    team_slug VARCHAR(50) REFERENCES payment.teams(team_slug),
    order_id VARCHAR(36) NOT NULL,
    amount DECIMAL(18,2) NOT NULL,
    currency VARCHAR(3) NOT NULL,
    status VARCHAR(20) NOT NULL,
    customer_key VARCHAR(36),
    customer_email VARCHAR(254),
    customer_phone VARCHAR(20),
    description TEXT,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT NOW()
);
```

## Error Handling

### Common Error Codes
- **2002**: Team slug already exists
- **2404**: Team not found
- **9999**: Internal error during processing

### HTTP Status Codes
- **200**: Success
- **201**: Created (team registration)
- **400**: Bad request (validation failed)
- **401**: Unauthorized (authentication failed)
- **404**: Not found
- **422**: Unprocessable entity (business rule violation)
- **429**: Too many requests (rate limited)
- **500**: Internal server error

## Testing Scripts

### Available Test Scripts

1. **test-api.sh** - Team registration lifecycle testing
2. **test-payment-lifecycle.sh** - Complete payment flow testing
3. **check-database-state.sh** - Database verification

### Running Tests

```bash
# Make scripts executable
chmod +x test-*.sh check-*.sh

# Test team registration
./test-api.sh

# Test complete payment lifecycle
./test-payment-lifecycle.sh

# Verify database state
./check-database-state.sh
```

## Configuration

### Environment-Specific Settings

**Development:**
```json
{
  "Api": {
    "BaseUrl": "http://localhost:5162",
    "Version": "v1"
  }
}
```

**Production:**
```json
{
  "Api": {
    "BaseUrl": "https://gateway.hackload.com",
    "Version": "v1"
  }
}
```

### Database Connection
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=task;Username=organizer;Password=password"
  }
}
```

## Integration Guidelines

### For Merchants

1. **Register Team**: Use admin API to register merchant team
2. **Store Credentials**: Securely store `teamSlug` and password
3. **Implement Token Generation**: Calculate HMAC-SHA256 tokens for API calls
4. **Handle Webhooks**: Implement notification URL endpoint for payment status updates
5. **Error Handling**: Implement proper error handling for all API responses

### For Payment Processing

1. **Initialize Payment**: Create payment session with customer details
2. **Redirect Customer**: Send customer to provided `paymentURL`
3. **Monitor Status**: Poll payment status or wait for webhook notifications
4. **Handle Results**: Process successful/failed payment outcomes
5. **Reconciliation**: Periodically verify payment statuses

## Security Considerations

1. **Token Security**: Never expose team secrets in client-side code
2. **HTTPS Only**: All production traffic must use HTTPS
3. **Rate Limiting**: Respect API rate limits to avoid blocking
4. **Input Validation**: Validate all input parameters
5. **Audit Logging**: All operations are logged for security auditing

## Monitoring and Metrics

The system includes Prometheus metrics for:
- Payment initialization requests
- Authentication attempts
- Payment processing duration
- Error rates by type
- Active payment sessions

## Troubleshooting

### Common Issues

1. **Authentication Failures**
   - Verify team slug and password
   - Check token calculation algorithm
   - Ensure request parameters match token calculation

2. **Database Connection Issues**
   - Verify PostgreSQL is running
   - Check connection string parameters
   - Confirm database schema exists

3. **Payment Processing Failures**
   - Check payment amount limits
   - Verify supported currencies
   - Review business rule violations

### Database Queries for Debugging

```sql
-- Check team registration
SELECT * FROM payment.teams WHERE team_slug = 'your-team-slug';

-- Check payment records
SELECT * FROM payment.payments WHERE team_slug = 'your-team-slug';

-- Check recent audit logs
SELECT * FROM payment.audit_logs 
WHERE table_name IN ('teams', 'payments') 
ORDER BY created_at DESC LIMIT 10;
```

## API Response Examples

### Successful Team Registration
```json
{
  "success": true,
  "message": "Team registered successfully",
  "teamSlug": "my-store-2025",
  "teamId": "550e8400-e29b-41d4-a716-446655440000",
  "passwordHashPreview": "a1b2c3d4",
  "status": "ACTIVE",
  "apiEndpoint": "http://localhost:5162/api/v1"
}
```

### Payment Initialization Response
```json
{
  "success": true,
  "paymentId": "pay_123456789",
  "paymentURL": "https://gateway.example.com/pay/123456789",
  "status": "PENDING",
  "expiresAt": "2025-08-07T12:00:00Z"
}
```

### Payment Status Response
```json
{
  "success": true,
  "paymentId": "pay_123456789",
  "orderId": "order-12345",
  "status": "CONFIRMED",
  "amount": 250000,
  "currency": "RUB",
  "confirmedAt": "2025-08-07T10:30:00Z"
}
```

---

*This documentation covers the current implementation as of August 2025. For the most up-to-date API specifications, refer to the Swagger documentation at `/swagger` when the application is running.*
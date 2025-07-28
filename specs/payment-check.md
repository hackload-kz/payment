# CheckOrder Method Technical Specification

## Overview
The CheckOrder method retrieves the current status of an order and all associated payment attempts. This method enables merchants to track payment progression through the lifecycle states shown in the payment flowchart, from initial creation through final settlement states.

## Function Signature
```
POST /checkOrder
Content-Type: application/json
```

## Request Parameters

### Required Parameters
- **TerminalKey** (string, required)
  - Terminal identifier issued to merchant in I-Business
  - Used for authentication and merchant identification

- **OrderId** (string, required)
  - Order identifier in merchant system
  - **Note**: Not a unique identifier - multiple payments can exist for same OrderId
  - Used to retrieve all payment attempts for a specific order

- **Token** (string, required)
  - Request signature for security validation
  - Generated using merchant's secret key and request parameters

## Response Format

### Response Schema
```json
Content-Type: application/json
```

### Response Parameters

#### Required Fields
- **TerminalKey** (string, required)
  - Terminal identifier
  - Echoed from request for validation

- **OrderId** (string, required)
  - Order identifier from merchant system
  - Echoed from request for correlation

- **Success** (boolean, required)
  - Request processing result
  - `true`: Successfully retrieved order status
  - `false`: Failed to retrieve order information

- **ErrorCode** (string, required)
  - Error code indicator
  - "0": Success
  - Non-zero: Specific error code for debugging

- **Payments** (Array of PaymentsCheckOrder objects, required)
  - Collection of all payment attempts for the specified OrderId
  - Each element represents a single payment session
  - May contain multiple entries if order had failed attempts or partial payments

#### Optional Fields
- **Message** (string, ≤255 chars)
  - Brief error description
  - Human-readable error summary
  - Present when Success = false

- **Details** (string)
  - Detailed error description
  - Technical error information for debugging
  - Additional context for error resolution

### PaymentsCheckOrder Object Structure
Each payment in the Payments array contains detailed information about individual payment attempts, including:
- Current payment status (maps to flowchart states)
- Payment identifiers and amounts
- Transaction timestamps
- Error information for failed attempts
- Authentication and processing details

## Response Examples

### Successful Order Status Retrieval
```json
{
  "TerminalKey": "1234567890",
  "OrderId": "order-12345",
  "Success": true,
  "ErrorCode": "0",
  "Payments": [
    {
      "PaymentId": "987654321",
      "Amount": 150000,
      "Status": "CONFIRMED",
      "CreatedDate": "2025-01-15T10:30:00+03:00",
      "CompletedDate": "2025-01-15T10:32:15+03:00"
    }
  ]
}
```

### Order Not Found
```json
{
  "TerminalKey": "1234567890",
  "OrderId": "nonexistent-order",
  "Success": false,
  "ErrorCode": "1004",
  "Message": "Order not found",
  "Details": "No payments found for OrderId 'nonexistent-order' in terminal '1234567890'",
  "Payments": []
}
```

### Multiple Payment Attempts
```json
{
  "TerminalKey": "1234567890",
  "OrderId": "order-retry-123",
  "Success": true,
  "ErrorCode": "0",
  "Payments": [
    {
      "PaymentId": "111111111",
      "Amount": 150000,
      "Status": "REJECTED",
      "CreatedDate": "2025-01-15T10:00:00+03:00",
      "ErrorCode": "2001"
    },
    {
      "PaymentId": "222222222",
      "Amount": 150000,
      "Status": "CONFIRMED",
      "CreatedDate": "2025-01-15T10:05:00+03:00",
      "CompletedDate": "2025-01-15T10:07:30+03:00"
    }
  ]
}
```

## Technical Implementation Notes

### Payment Status Mapping
The Status field in each payment corresponds directly to the states in the payment lifecycle flowchart:
- **NEW**: Payment initialized but not yet processed
- **FORM_SHOWED**: Payment form displayed to customer
- **AUTHORIZING**: Payment being processed by bank
- **3DS_CHECKING**: 3D Secure authentication in progress
- **AUTHORIZED**: Payment authorized, awaiting confirmation
- **CONFIRMED**: Payment successfully completed
- **REJECTED**: Payment declined by bank
- **CANCELLED**: Payment cancelled by system or merchant
- **REVERSED**: Payment reversed (full or partial)
- **REFUNDED**: Payment refunded (full or partial)

### Use Cases
1. **Order Status Tracking**: Monitor payment progression through lifecycle states
2. **Retry Logic**: Identify failed payments and initiate new attempts
3. **Reconciliation**: Match merchant records with acquiring system states  
4. **Customer Support**: Investigate payment issues and status inquiries
5. **Webhook Validation**: Verify notification authenticity against system state

### Integration Considerations
- Method returns ALL payment attempts for an OrderId, not just the latest
- Multiple payments may exist due to retries, partial payments, or refunds
- Status progression follows the payment lifecycle flowchart sequence
- Use PaymentId from response for specific payment operations
- Implement polling mechanism for real-time status updates if webhooks unavailable

### Security Notes
- Token validation required for all requests
- Only returns payments associated with requesting terminal
- No sensitive card data included in response
- HTTPS required for all communications

This method provides comprehensive order status visibility, enabling merchants to track payments through the complete lifecycle from initialization through final settlement or cancellation.